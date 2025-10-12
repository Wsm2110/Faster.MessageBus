using Faster.Transport.Primitives;
using System.Buffers;
using System.Buffers.Binary;

public sealed class FrameParserRing : IDisposable
{
    private readonly byte[] _buffer;
    private readonly byte[] _tempBuffer;
    private readonly SpscRingBuffer<ReadOnlyMemory<byte>> _frames = new(4096);
    private readonly int _capacity;
    private readonly int _mask; // _capacity - 1 (for bitmask wrapping)
    private int _head;
    private int _tail;
    private int _length;

    public FrameParserRing(int capacity = 65536, int tempBufferSize = 65536)
    {
        if (!IsPowerOfTwo(capacity))
            throw new ArgumentException("Capacity must be a power of two.", nameof(capacity));

        _capacity = capacity;
        _mask = capacity - 1;
        _buffer = ArrayPool<byte>.Shared.Rent(capacity);
        _tempBuffer = ArrayPool<byte>.Shared.Rent(tempBufferSize);
        _head = 0;
        _tail = 0;
        _length = 0;
    }

    public void Feed(ReadOnlySpan<byte> data)
    {
        int dataPos = 0;

        while (dataPos < data.Length)
        {
            int writable = Math.Min(data.Length - dataPos, _capacity - _length);
            if (writable == 0)
                throw new InvalidOperationException("FrameParserRing buffer overflow — frame too large for capacity.");

            int firstPart = Math.Min(writable, _capacity - _head);
            data.Slice(dataPos, firstPart).CopyTo(_buffer.AsSpan(_head, firstPart));

            int remaining = writable - firstPart;
            if (remaining > 0)
                data.Slice(dataPos + firstPart, remaining).CopyTo(_buffer.AsSpan(0, remaining));

            _head = (_head + writable) & _mask;
            _length += writable;
            dataPos += writable;

            ParseFrames();
        }
    }

    public bool TryReadFrame(out ReadOnlyMemory<byte> frame) => _frames.TryDequeue(out frame);

    private void ParseFrames()
    {
        while (_length >= 4)
        {
            int len;
            if (_tail + 4 <= _capacity)
            {
                len = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(_tail, 4));
            }
            else
            {
                Span<byte> tmp = stackalloc byte[4];
                int first = _capacity - _tail;
                _buffer.AsSpan(_tail, first).CopyTo(tmp);
                _buffer.AsSpan(0, 4 - first).CopyTo(tmp[first..]);
                len = BinaryPrimitives.ReadInt32LittleEndian(tmp);
            }

            if (len <= 0 || len > _capacity)
            {
                Reset();
                throw new InvalidOperationException($"Corrupted frame length: {len}");
            }

            if (_length < 4 + len)
                break;

            int headerEnd = (_tail + 4) & _mask;
            int availableAfterHeader = _capacity - headerEnd;

            ReadOnlyMemory<byte> frame;

            if (headerEnd + len <= _capacity)
            {
                frame = new ReadOnlyMemory<byte>(_buffer, headerEnd, len);
            }
            else
            {
                int firstPart = Math.Min(len, availableAfterHeader);
                int secondPart = len - firstPart;

                _buffer.AsSpan(headerEnd, firstPart).CopyTo(_tempBuffer.AsSpan(0, firstPart));
                if (secondPart > 0)
                    _buffer.AsSpan(0, secondPart).CopyTo(_tempBuffer.AsSpan(firstPart));

                frame = new ReadOnlyMemory<byte>(_tempBuffer, 0, len);
            }

            SpinWait sw = new();
            while (!_frames.TryEnqueue(frame))
            {
                sw.SpinOnce();
            }

            _tail = (_tail + 4 + len) & _mask;
            _length -= 4 + len;
        }
    }

    public void Reset()
    {
        _head = 0;
        _tail = 0;
        _length = 0;
        _frames.Clear();
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        ArrayPool<byte>.Shared.Return(_tempBuffer);
    }

    private static bool IsPowerOfTwo(int value) => (value & (value - 1)) == 0;
}
