using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Shared;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

public static class WindowsNetMqOptimizer
{
    // --- Timer Resolution ---
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    // --- Thread Affinity ---
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    // --- Thread Priority ---
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

    private const int THREAD_PRIORITY_HIGHEST = 2;

    // --- NUMA Support ---
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumaHighestNodeNumber(out uint HighestNodeNumber);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumaNodeProcessorMaskEx(
        ushort Node,
        out GROUP_AFFINITY ProcessorMask
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct GROUP_AFFINITY
    {
        public UIntPtr Mask; // bitmask of processors
        public ushort Group;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ushort[] Reserved;
    }

    private static bool _timerBoosted;

    private static readonly List<int> _numaCores = new();
    private static int _nextCoreIndex = 0;
    private static readonly object _lock = new();

    /// <summary>
    /// Boost system-wide timer resolution (1ms).
    /// </summary>
    public static void BoostSystemTimer()
    {
        if (!_timerBoosted)
        {
            TimeBeginPeriod(1);
            _timerBoosted = true;
        }
    }

    /// <summary>
    /// Restore system timer resolution.
    /// </summary>
    public static void RestoreSystemTimer()
    {
        if (_timerBoosted)
        {
            TimeEndPeriod(1);
            _timerBoosted = false;
        }
    }

    /// <summary>
    /// Pin current thread to a specific CPU core.
    /// </summary>
    public static void PinThreadToCore(int coreIndex)
    {
        var mask = (UIntPtr)(1UL << coreIndex);
        SetThreadAffinityMask(GetCurrentThread(), mask);
    }

    /// <summary>
    /// Elevate current thread priority to highest.
    /// </summary>
    public static void ElevateThreadPriority()
    {
        var thread = GetCurrentThread();
        SetThreadPriority(thread, THREAD_PRIORITY_HIGHEST);
    }

    /// <summary>
    /// Discover all cores across NUMA nodes.
    /// </summary>
    private static void DiscoverNumaCores()
    {
        if (_numaCores.Count > 0) return;

        if (!GetNumaHighestNodeNumber(out uint highestNode))
            return;

        for (ushort node = 0; node <= highestNode; node++)
        {
            if (GetNumaNodeProcessorMaskEx(node, out var mask))
            {
                ulong cpuMask = (ulong)mask.Mask;

                while (cpuMask != 0)
                {
#if NET48
int coreIndex = TrailingZeroCount((long)cpuMask);
#else
                    int coreIndex = BitOperations.TrailingZeroCount(cpuMask);
#endif
                    _numaCores.Add(coreIndex);
                    cpuMask &= ~(1UL << coreIndex); // clear the bit
                }
            }
        }
    }

    /// <summary>
    /// Auto-assign the next available NUMA-local core (round robin).
    /// </summary>
    public static int AssignNextCore()
    {
        lock (_lock)
        {
            DiscoverNumaCores();
            if (_numaCores.Count == 0) return 0;

            int core = _numaCores[_nextCoreIndex % _numaCores.Count];
            _nextCoreIndex++;
            return core;
        }
    }

    /// <summary>
    /// Apply full optimization for a NetMQ poller thread.
    /// </summary>
    public static void OptimizePollerThread(bool numaAware = true, int? coreIndex = null)
    {
        BoostSystemTimer();
        ElevateThreadPriority();

        int targetCore = coreIndex ?? (numaAware ? AssignNextCore() : 0);
        PinThreadToCore(targetCore);
    }

    // Optional: NUMA-aware memory pool
    // For now, we just wrap ArrayPool. Real binding would need VirtualAllocExNuma.
    public static ArrayPool<byte> CreateNumaLocalBufferPool()
    {
        // Placeholder — standard ArrayPool is used,
        // but could be replaced with VirtualAllocExNuma for real NUMA locality.
        return ArrayPool<byte>.Shared;
    }

    /// Finds the bit index of the least significant '1' in a 64-bit integer.
    /// This is a polyfill for BitOperations.TrailingZeroCount for .NET Framework.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>The 0-based index of the first set bit, or 64 if no bits are set.</returns>
    public static int TrailingZeroCount(long value)
    {
        if (value == 0)
        {
            return 64; // No bits are set, standard behavior.
        }

        int count = 0;
        // Keep shifting right until the last bit is a '1'
        while ((value & 1) == 0)
        {
            value >>= 1;
            count++;
        }
        return count;
    }
}

