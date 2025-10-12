using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Faster.Transport.Primitives
{
    public sealed class MpscQueue<T>
    {
        private readonly Slot[] _buffer;
        private readonly int _mask;
        private PaddedLong _head; // consumer
        private PaddedLong _tail; // producers
        private PaddedInt _count;

        private struct Slot { public long Sequence; public T Value; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPowerOfTwo(int x) { int p = 1; while (p < x) p <<= 1; return p; }

        public MpscQueue(int capacity)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            int cap = NextPowerOfTwo(capacity);
            _buffer = new Slot[cap];
            for (int i = 0; i < cap; i++) _buffer[i].Sequence = i;
            _mask = cap - 1;
            _head = new PaddedLong(0);
            _tail = new PaddedLong(0);
            _count = new PaddedInt(0);
        }

        public int Capacity => _buffer.Length;
        public int Count => Volatile.Read(ref _count.Value);
        public bool IsEmpty => Count == 0;
        public bool IsFull  => Count >= Capacity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnqueue(T item)
        {
            var buffer = _buffer;
            var mask = _mask;

            for (;;)
            {
                var pos = Volatile.Read(ref _tail.Value);
                ref var slot = ref buffer[(int)(pos & mask)];

                var seq = Volatile.Read(ref slot.Sequence);
                var diff = seq - pos;

                if (diff == 0)
                {
                    if (Interlocked.CompareExchange(ref _tail.Value, pos + 1, pos) == pos)
                    {
                        slot.Value = item;
                        Volatile.Write(ref slot.Sequence, pos + 1);
                        Interlocked.Increment(ref _count.Value);
                        return true;
                    }
                    continue;
                }

                if (diff < 0) return false; // full
                Thread.SpinWait(1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue(out T item)
        {
            var buffer = _buffer;
            var mask = _mask;

            var pos = Volatile.Read(ref _head.Value);
            ref var slot = ref buffer[(int)(pos & mask)];

            var seq = Volatile.Read(ref slot.Sequence);
            var diff = seq - (pos + 1);

            if (diff == 0)
            {
                item = slot.Value;
                Volatile.Write(ref slot.Sequence, pos + buffer.Length);
                Volatile.Write(ref _head.Value, pos + 1);
                Interlocked.Decrement(ref _count.Value);
                return true;
            }

            item = default!;
            return false;
        }

        public int Drain(T[] buffer, int max)
        {
            int n = 0;
            while (n < max && TryDequeue(out var item))
            {
                buffer[n++] = item;
            }
            return n;
        }

        private struct PaddedLong
        {
            public long Value;
#pragma warning disable CS0169
            private long p1, p2, p3, p4, p5, p6, p7;
#pragma warning restore CS0169
            public PaddedLong(long v) => Value = v;
        }
        private struct PaddedInt
        {
            public int Value;
#pragma warning disable CS0169
            private long p1, p2, p3, p4, p5, p6, p7;
#pragma warning restore CS0169
            public PaddedInt(int v) => Value = v;
        }
    }
}
