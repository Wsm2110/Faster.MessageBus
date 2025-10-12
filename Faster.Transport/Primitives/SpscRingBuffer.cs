using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Faster.Transport.Primitives
{
    public sealed class SpscRingBuffer<T>
    {
        private readonly T[] _buffer;
        private readonly int _mask;
        private PaddedLong _head; // consumer index
        private PaddedLong _tail; // producer index
        private PaddedInt _count;

        public int Capacity => _buffer.Length;
        public int Count => Volatile.Read(ref _count.Value);
        public bool IsEmpty => Count == 0;
        public bool IsFull => Count >= Capacity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPowerOfTwo(int x) { int p = 1; while (p < x) p <<= 1; return p; }

        public SpscRingBuffer(int capacity)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            int cap = NextPowerOfTwo(capacity);
            _buffer = new T[cap];
            _mask = cap - 1;
            _head = new PaddedLong(0);
            _tail = new PaddedLong(0);
            _count = new PaddedInt(0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnqueue(T item)
        {
            long head = Volatile.Read(ref _head.Value);
            long tail = _tail.Value;
            if (tail - head == _buffer.Length) return false;
            _buffer[(int)(tail & _mask)] = item;
            Volatile.Write(ref _tail.Value, tail + 1);
            Interlocked.Increment(ref _count.Value);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue(out T item)
        {
            long tail = Volatile.Read(ref _tail.Value);
            long head = _head.Value;
            if (tail == head) { item = default!; return false; }
            int index = (int)(head & _mask);
            item = _buffer[index];
            _buffer[index] = default!;
            Volatile.Write(ref _head.Value, head + 1);
            Interlocked.Decrement(ref _count.Value);
            return true;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head.Value = 0;
            _tail.Value = 0;
            Volatile.Write(ref _count.Value, 0);
        }

        private struct PaddedLong
        {
            public long Value;
#pragma warning disable CS0169
            private long p1, p2, p3, p4, p5, p6, p7;
#pragma warning restore CS0169
            public PaddedLong(long value) => Value = value;
        }

        private struct PaddedInt
        {
            public int Value;
#pragma warning disable CS0169
            private long p1, p2, p3, p4, p5, p6, p7;
#pragma warning restore CS0169
            public PaddedInt(int value) => Value = value;
        }
    }
}
