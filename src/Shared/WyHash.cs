namespace Faster.MessageBus.Shared;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class WyHash
{
    private static readonly ulong[] Secret = {
        0xa0761d6478bd642fUL, 0xe7037ed1a0b428dbUL,
        0x8ebc6af09c88c6e3UL, 0x589965cc75374cc3UL
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Mum(ref ulong a, ref ulong b)
    {
        ulong ha = a >> 32, hb = b >> 32;
        ulong la = (uint)a, lb = (uint)b;
        ulong rh = ha * hb, rm0 = ha * lb, rm1 = hb * la, rl = la * lb;

        ulong t = rl + (rm0 << 32);
        ulong c = (t < rl) ? 1UL : 0UL;
        ulong lo = t + (rm1 << 32);
        c += (lo < t) ? 1UL : 0UL;
        ulong hi = rh + (rm0 >> 32) + (rm1 >> 32) + c;

        a = lo;
        b = hi;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Mix(ulong a, ulong b)
    {
        Mum(ref a, ref b);
        return a ^ b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(ReadOnlySpan<byte> key)
    {
        ulong seed = Secret[0];
        ulong a = 0, b = 0;
        int len = key.Length;
        ref byte start = ref MemoryMarshal.GetReference(key);
        ref var start64 = ref Unsafe.As<byte, ulong>(ref start);
        ref var start32 = ref Unsafe.As<byte, uint>(ref start);

        if (len <= 16)
        {
            if (len >= 4)
            {
                a = ((ulong)Unsafe.Add(ref start32, 0) << 32) | Unsafe.Add(ref start32, (len >> 3));
                b = ((ulong)Unsafe.Add(ref start32, len / 4 - 1) << 32) | Unsafe.Add(ref start32, len / 4 - 2);
            }
            else if (len > 0)
            {
                a = ((ulong)start << 16) | ((ulong)Unsafe.Add(ref start, len >> 1) << 8) | Unsafe.Add(ref start, len - 1);
            }
        }
        else
        {
            int i = len;
            while (i > 16)
            {
                seed = Mix(Unsafe.Add(ref start64, 0) ^ Secret[1], Unsafe.Add(ref start64, 1) ^ seed);
                start64 = ref Unsafe.Add(ref start64, 2);
                i -= 16;
            }
            a = Unsafe.Add(ref start64, i / 8 - 2);
            b = Unsafe.Add(ref start64, i / 8 - 1);
        }

        return Mix(Secret[1] ^ (ulong)len, Mix(a ^ Secret[1], b ^ seed));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return Hash(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(ulong x) => Mix(x, 0x9E3779B97F4A7C15UL);
}

/// <summary>
/// A pseudo-random number generator based on the wyrand algorithm.
/// This class maintains an internal state (seed) to generate a sequence of numbers.
/// </summary>
public class WyRandom
{
    // The internal state of the generator.
    private ulong _seed;

    /// <summary>
    /// Initializes a new instance of the WyRandom class using a specified seed.
    /// </summary>
    /// <param name="seed">The initial state for the random number generator.</param>
    public WyRandom(ulong seed)
    {
        _seed = seed;
    }

    /// <summary>
    /// Returns a non-negative random 64-bit unsigned integer.
    /// This is the core generator function.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong NextInt64()
    {
        // The wyrand algorithm: increment the seed and mix it with a constant.
        // We use the constants from the private 'Secret' array in WyHash.
        // 0xa0761d6478bd642fUL is Secret[0]
        // 0xe7037ed1a0b428dbUL is Secret[1]
        _seed += 0xa0761d6478bd642fUL;
        return WyHash.Mix(_seed, 0xe7037ed1a0b428dbUL);
    }

    /// <summary>
    /// Returns a non-negative random 32-bit signed integer.
    /// </summary>
    /// <returns>A 32-bit signed integer that is greater than or equal to 0.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Next()
    {
        // We take the upper 31 bits of the 64-bit result to get a positive integer.
        return (int)(NextInt64() >> 33);
    }
}


