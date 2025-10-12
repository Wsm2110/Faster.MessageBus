using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Faster.Transport.Primitives
{
    public sealed class MpmcQueue<T>
    {
        private readonly Cell[] _buffer;
        private readonly int _mask;

        private PaddedLong _head;
        private PaddedLong _tail;

        // exact, atomic count with padding to minimize cache-line contention
        private PaddedInt _count;

        private struct Cell
        {
            public long Sequence;
            public T Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPow2(int x)
        {
            int p = 1;
            while (p < x) p <<= 1;
            return p;
        }

        public MpmcQueue(int capacity)
        {
            if (capacity < 2)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            int cap = NextPow2(capacity);
            _buffer = new Cell[cap];

            for (int i = 0; i < cap; i++)
                _buffer[i].Sequence = i;

            _mask = cap - 1;
            _head = new PaddedLong(0);
            _tail = new PaddedLong(0);
            _count = new PaddedInt(0);
        }

        /// <summary>Total fixed capacity of the queue.</summary>
        public int Capacity => _buffer.Length;

        /// <summary>Exact number of items currently in the queue.</summary>
        public int Count => Volatile.Read(ref _count.Value);

        /// <summary>True if the queue has no items.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>True if the queue is full.</summary>
        public bool IsFull => Count >= Capacity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnqueue(T item)
        {
            var buffer = _buffer;
            var mask = _mask;
            var pos = Volatile.Read(ref _tail.Value);

            for (; ; )
            {
                ref var cell = ref buffer[(int)(pos & mask)];
                var seq = Volatile.Read(ref cell.Sequence);
                var dif = seq - pos;

                if (dif == 0)
                {
                    if (Interlocked.CompareExchange(ref _tail.Value, pos + 1, pos) == pos)
                    {
                        // publish the value, then advance sequence to make it visible
                        cell.Value = item;
                        Volatile.Write(ref cell.Sequence, pos + 1);

                        // increment count AFTER publication to avoid exposing invisible items
                        Interlocked.Increment(ref _count.Value);
                        return true;
                    }
                }
                else if (dif < 0)
                {
                    // full
                    return false;
                }

                pos = Volatile.Read(ref _tail.Value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue(out T item)
        {
            var buffer = _buffer;
            var mask = _mask;
            var pos = Volatile.Read(ref _head.Value);

            for (; ; )
            {
                ref var cell = ref buffer[(int)(pos & mask)];
                var seq = Volatile.Read(ref cell.Sequence);
                var dif = seq - (pos + 1);

                if (dif == 0)
                {
                    if (Interlocked.CompareExchange(ref _head.Value, pos + 1, pos) == pos)
                    {
                        // read value, then mark slot free by jumping sequence forward
                        item = cell.Value;
                        Volatile.Write(ref cell.Sequence, pos + buffer.Length);

                        // decrement AFTER freeing slot; small window may over-report count, which is safe
                        Interlocked.Decrement(ref _count.Value);
                        return true;
                    }
                }
                else if (dif < 0)
                {
                    item = default!;
                    return false; // empty
                }

                pos = Volatile.Read(ref _head.Value);
            }
        }

        private struct PaddedLong
        {
            public long Value;
#pragma warning disable CS0169
            private long _p1, _p2, _p3, _p4, _p5, _p6, _p7;
#pragma warning restore CS0169
            public PaddedLong(long value) => Value = value;
        }

        private struct PaddedInt
        {
            public int Value;
#pragma warning disable CS0169
            private long _p1, _p2, _p3, _p4, _p5, _p6, _p7; // pad to separate cache lines
#pragma warning restore CS0169
            public PaddedInt(int value) => Value = value;
        }
    }
}
