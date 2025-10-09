using CommunityToolkit.HighPerformance.Buffers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Commands.Shared;

public class ThreadLocalBufferWriter
{
    private readonly ThreadLocal<ArrayPoolBufferWriter<byte>> _threadWriters =
        new(() => new ArrayPoolBufferWriter<byte>(1024));

    public ArrayPoolBufferWriter<byte> Writer => _threadWriters.Value;
}

public class VyukovMPMCQueue
{
    private class Cell
    {
        public long Sequence;
        public PendingReply Value;
    }

    private readonly Cell[] _buffer;
    private readonly int _bufferMask;

    private PaddedLong _enqueuePos = new PaddedLong(0);
    private PaddedLong _dequeuePos = new PaddedLong(0);

    public VyukovMPMCQueue(int capacity)
    {
        if (capacity < 2 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentException("Capacity must be a power of 2 and >= 2", nameof(capacity));

        _buffer = new Cell[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _buffer[i] = new Cell { Sequence = i, };
        }
        _bufferMask = capacity - 1;
    }

    public bool Enqueue(PendingReply item)
    {
        Cell cell = null;
        long pos = 0;

        while (true)
        {
            pos = Volatile.Read(ref _enqueuePos.Value);
            cell = _buffer[pos & _bufferMask];
            long seq = Volatile.Read(ref cell.Sequence);
            long dif = seq - pos;

            if (dif == 0)
            {
                if (Interlocked.CompareExchange(ref _enqueuePos.Value, pos + 1, pos) == pos)
                    break;
            }
            else if (dif < 0)
            {
                return false; // full
            }
            else
            {
                Thread.SpinWait(1);
            }
        }

        cell.Value = item;
        Volatile.Write(ref cell.Sequence, pos + 1);
        return true;
    }

    public bool Dequeue(out PendingReply result)
    {
        Cell cell = null;
        long pos = 0;

        while (true)
        {
            pos = Volatile.Read(ref _dequeuePos.Value);
            cell = _buffer[pos & _bufferMask];
            long seq = Volatile.Read(ref cell.Sequence);
            long dif = seq - (pos + 1);

            if (dif == 0)
            {
                if (Interlocked.CompareExchange(ref _dequeuePos.Value, pos + 1, pos) == pos)
                    break;
            }
            else if (dif < 0)
            {
                result = default!;
                return false; // empty
            }
            else
            {
                Thread.SpinWait(1);
            }
        }

        result = cell.Value;
        Volatile.Write(ref cell.Sequence, pos + _bufferMask + 1);
        return true;
    }

    // Padding to avoid false sharing
    private sealed class PaddedLong
    {
        public long Value;
        private long p1, p2, p3, p4, p5, p6, p7;
        public PaddedLong(long value) { Value = value; }
    }
}

public class ObjectPool
{
    private readonly VyukovMPMCQueue _queue;

    public ObjectPool(int capacity)
    {
        _queue = new VyukovMPMCQueue(capacity);
        for (int i = 0; i < capacity; i++)
        {
            _queue.Enqueue(new PendingReply());
        }
    }

    /// <summary>
    /// Take an object from the pool, or create a new one if empty.
    /// </summary>
    public PendingReply Rent()
    {
        if (_queue.Dequeue(out var item))
            return item;

        return new PendingReply();
    }

    /// <summary>
    /// Return an object to the pool. If full, the object is discarded.
    /// </summary>
    public void Return(PendingReply item)
    {
        item.Reset();
        _queue.Enqueue(item);
    }
}
