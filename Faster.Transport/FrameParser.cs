using System;
using System.Buffers;
using System.Buffers.Binary;

/// <summary>
/// High-performance, zero-allocation frame parser using a circular (ring) buffer.
/// Each frame is prefixed with a 4-byte little-endian length header.
/// </summary>
/// <remarks>
/// Designed for TCP streams where frame boundaries are not guaranteed.
/// Efficiently handles frames that span across the end of the buffer.
/// </remarks>
public sealed class FrameParserRing : IDisposable
{
    private readonly byte[] _buffer;
    private readonly byte[] _tempBuffer; // reused for wrapped frames
    private int _head;   // write position
    private int _tail;   // read position
    private int _length; // bytes currently in buffer
    private readonly int _capacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameParserRing"/> class.
    /// </summary>
    /// <param name="capacity">Total ring buffer capacity (default 64 KB).</param>
    /// <param name="tempBufferSize">Reusable buffer for frames that wrap around (default 64 KB).</param>
    public FrameParserRing(int capacity = 65536, int tempBufferSize = 65536)
    {
        _capacity = capacity;
        _buffer = ArrayPool<byte>.Shared.Rent(capacity);
        _tempBuffer = ArrayPool<byte>.Shared.Rent(tempBufferSize);
        _head = 0;
        _tail = 0;
        _length = 0;
    }

    /// <summary>
    /// Feeds raw bytes into the parser.
    /// Emits complete frames via the <paramref name="onFrame"/> callback.
    /// </summary>
    /// <param name="data">Span of received data to append.</param>
    /// <param name="onFrame">Callback invoked for each complete frame.</param>
    /// <exception cref="InvalidOperationException">Thrown if the input exceeds buffer capacity.</exception>
    public void Feed(ReadOnlySpan<byte> data, Action<ReadOnlyMemory<byte>> onFrame)
    {
        int dataPos = 0;

        while (dataPos < data.Length)
        {
            int writable = Math.Min(data.Length - dataPos, _capacity - _length);
            if (writable == 0)
                throw new InvalidOperationException("FrameParserRing buffer overflow — frame too large for capacity.");

            // Copy into ring (wrap if needed)
            int firstPart = Math.Min(writable, _capacity - _head);
            data.Slice(dataPos, firstPart).CopyTo(_buffer.AsSpan(_head, firstPart));

            int remaining = writable - firstPart;
            if (remaining > 0)
                data.Slice(dataPos + firstPart, remaining).CopyTo(_buffer.AsSpan(0, remaining));

            _head = (_head + writable) % _capacity;
            _length += writable;
            dataPos += writable;

            // Try to parse complete frames
            ParseFrames(onFrame);
        }
    }

    /// <summary>
    /// Extracts and emits complete frames from the ring buffer.
    /// </summary>
    private void ParseFrames(Action<ReadOnlyMemory<byte>> onFrame)
    {
        while (_length >= 4)
        {
            // Read 4-byte little-endian frame length (may wrap)
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
                _buffer.AsSpan(0, 4 - first).CopyTo(tmp.Slice(first));
                len = BinaryPrimitives.ReadInt32LittleEndian(tmp);
            }

            // Validate frame size
            if (len <= 0 || len > _capacity)
            {
                Reset();
                throw new InvalidOperationException($"Corrupted frame length: {len}");
            }

            // Wait for full frame data
            if (_length < 4 + len)
                break;

            int headerEnd = (_tail + 4) % _capacity;
            int availableAfterHeader = _capacity - headerEnd;

            ReadOnlyMemory<byte> frame;

            if (headerEnd + len <= _capacity)
            {
                // Contiguous frame
                frame = new ReadOnlyMemory<byte>(_buffer, headerEnd, len);
            }
            else
            {
                // Wrapped frame
                int firstPart = Math.Min(len, availableAfterHeader);
                int secondPart = len - firstPart;

                _buffer.AsSpan(headerEnd, firstPart).CopyTo(_tempBuffer.AsSpan(0, firstPart));
                if (secondPart > 0)
                    _buffer.AsSpan(0, secondPart).CopyTo(_tempBuffer.AsSpan(firstPart));

                frame = new ReadOnlyMemory<byte>(_tempBuffer, 0, len);
            }

            // Deliver frame
            onFrame(frame);

            // Advance
            _tail = (_tail + 4 + len) % _capacity;
            _length -= 4 + len;
        }
    }

    /// <summary>
    /// Clears the buffer and resets parser state.
    /// </summary>
    public void Reset()
    {
        _head = 0;
        _tail = 0;
        _length = 0;
    }

    /// <summary>
    /// Releases all buffers back to the shared pool.
    /// </summary>
    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        ArrayPool<byte>.Shared.Return(_tempBuffer);
    }
}
