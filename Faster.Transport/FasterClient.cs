namespace Faster.Transport;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// A high-performance, zero-allocation TCP client optimized for framed message protocols.
/// Uses <see cref="SocketAsyncEventArgs"/> for overlapped I/O and <see cref="Channel{T}"/> for thread-safe send queuing.
/// </summary>
/// <remarks>
/// Each message is sent with a 4-byte little-endian length prefix followed by the payload.
/// The class provides fire-and-forget async send behavior and frame-based receive callbacks.
/// Designed for extreme throughput and low latency messaging systems.
/// </remarks>
public sealed class FasterClient : IDisposable
{
    private readonly Socket _socket;
    private readonly SocketAsyncEventArgs _sendArgs;
    private readonly SocketAsyncEventArgs _recvArgs;
    private readonly Channel<SendBuffer> _sendQueue;
    private readonly FrameParserRing _parser;
    private readonly CancellationTokenSource _cts = new();
    private int _sending;
    private volatile bool _isDisposed;

    private const int MaxFrameSize = 1024 * 1024; // 1 MB limit
    private const int DefaultSendQueueCapacity = 4096; // tune based on expected load

    /// <summary>
    /// Triggered when a complete frame has been received and parsed successfully.
    /// </summary>
    public event Action<FasterClient, ReadOnlyMemory<byte>>? OnReceived;

    /// <summary>
    /// Triggered when the socket disconnects or an error occurs.
    /// </summary>
    public event Action<FasterClient, Exception?>? Disconnected;

    // ───────────────────────────────
    // Constructors
    // ───────────────────────────────

    public FasterClient(EndPoint remoteEndPoint, int recvBufferSize = 8192, int frameBufferSize = 65536)
        : this(CreateAndConnect(remoteEndPoint), recvBufferSize, frameBufferSize)
    { }

    public FasterClient(Socket connectedSocket, int recvBufferSize = 8192, int frameBufferSize = 65536)
    {
        _socket = connectedSocket ?? throw new ArgumentNullException(nameof(connectedSocket));
        _socket.NoDelay = true;
        _socket.ReceiveBufferSize = 1024 * 1024;
        _socket.SendBufferSize = 1024 * 1024;

        _parser = new(frameBufferSize);

        _sendArgs = new SocketAsyncEventArgs();
        _sendArgs.Completed += IOCompleted;

        _recvArgs = new SocketAsyncEventArgs();
        _recvArgs.Completed += IOCompleted;
        _recvArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(recvBufferSize), 0, recvBufferSize);

        // 🔒 BoundedChannel with tuned capacity for backpressure and fairness
        _sendQueue = Channel.CreateBounded<SendBuffer>(new BoundedChannelOptions(DefaultSendQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait // blocks writers when full → backpressure instead of dropping
        });

        _ = SendLoopAsync();
        StartReceive();
    }

    private static Socket CreateAndConnect(EndPoint remoteEndPoint)
    {
        var socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(remoteEndPoint);
        socket.NoDelay = true;
        return socket;
    }

    /// <summary>
    /// Queues a message to be sent asynchronously. 
    /// </summary>
    public bool TrySend(ReadOnlyMemory<byte> payload)
    {
        if (_cts.IsCancellationRequested || _isDisposed)
        {
            return false;
        }

        var buf = ArrayPool<byte>.Shared.Rent(payload.Length + 4);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), payload.Length);
        payload.CopyTo(buf.AsMemory(4));

        // Avoid blocking — prefer TryWrite for high-frequency paths
        if (!_sendQueue.Writer.TryWrite(new SendBuffer(buf, payload.Length + 4)))
        {
            ArrayPool<byte>.Shared.Return(buf);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Asynchronous version of sending — will apply backpressure when the queue is full.
    /// </summary>
    public async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (_cts.IsCancellationRequested || _isDisposed)
            return false;

        var buf = ArrayPool<byte>.Shared.Rent(payload.Length + 4);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), payload.Length);
        payload.CopyTo(buf.AsMemory(4));

        try
        {
            await _sendQueue.Writer.WriteAsync(new SendBuffer(buf, payload.Length + 4), cancellationToken)
                                  .ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            ArrayPool<byte>.Shared.Return(buf);
            return false;
        }
        catch (OperationCanceledException)
        {
            ArrayPool<byte>.Shared.Return(buf);
            return false;
        }
    }

    private async Task SendLoopAsync()
    {
        await foreach (var sb in _sendQueue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
        {
            if (_isDisposed)
            {
                ArrayPool<byte>.Shared.Return(sb.Buffer);
                break;
            }

            // Spin until previous send completes
            if (_cts.IsCancellationRequested)
            {
                ArrayPool<byte>.Shared.Return(sb.Buffer);
                return;
            }

            // Wait until the previous send completes
            var spinner = new SpinWait();
            while (!SetSending())
            {
                spinner.SpinOnce();
                if (_cts.IsCancellationRequested)
                { 
                    return;
                }
            }

            try
            {
                _sendArgs.SetBuffer(sb.Buffer, 0, sb.Length);
                _sendArgs.UserToken = sb;

                bool pending = _socket.SendAsync(_sendArgs);
                if (!pending)
                {
                    // Completed synchronously
                    ProcessSend(_sendArgs);               
                }
            }
            catch (ObjectDisposedException)
            {
                ArrayPool<byte>.Shared.Return(sb.Buffer);
                Interlocked.Exchange(ref _sending, 0);
                break;
            }
            catch (Exception ex)
            {
                ArrayPool<byte>.Shared.Return(sb.Buffer);
                Interlocked.Exchange(ref _sending, 0);
                Close(ex);
                return;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool SetSending()
    {
        // Returns true if we successfully set _sending = 1 (i.e., we acquired the send slot)
        return Interlocked.CompareExchange(ref _sending, 1, 0) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetSending()
    {
        // Marks the send slot as free again
        Interlocked.Exchange(ref _sending, 0);
    }

    private bool IsSending => Volatile.Read(ref _sending) == 1;

    private void ProcessSend(SocketAsyncEventArgs e)
    {     
        if (e.UserToken is SendBuffer sb)
        {
            ArrayPool<byte>.Shared.Return(sb.Buffer);
            e.UserToken = null;
        }

        if (e.SocketError != SocketError.Success)
        {
            Close(new SocketException((int)e.SocketError));
            return;
        }

        Interlocked.Exchange(ref _sending, 0);
    }

    private void StartReceive()
    {
        bool pending = _socket.ReceiveAsync(_recvArgs);
        if (!pending)
            ProcessReceive(_recvArgs);
    }

    private void ProcessReceive(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
        {
            Close(e.SocketError == SocketError.Success ? null : new SocketException((int)e.SocketError));
            return;
        }

        var data = e.Buffer.AsSpan(e.Offset, e.BytesTransferred);

        try
        {
            _parser.Feed(data, frame =>
            {
                if (frame.Length > MaxFrameSize)
                    throw new InvalidOperationException($"Frame exceeds max size {MaxFrameSize} bytes.");
                OnReceived?.Invoke(this, frame);
            });
        }
        catch (Exception ex)
        {
            Close(ex);
            return;
        }

        bool pending = _socket.ReceiveAsync(e);
        if (!pending)
            ProcessReceive(e);
    }


    private void IOCompleted(object? sender, SocketAsyncEventArgs e)
    {
        switch (e.LastOperation)
        {
            case SocketAsyncOperation.Send: ProcessSend(e); break;
            case SocketAsyncOperation.Receive: ProcessReceive(e); break;
        }
    }

    private void Close(Exception? ex = null)
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        try { _cts.Cancel(); } catch { }
        try { _socket?.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket?.Dispose(); } catch { }
        try { _sendArgs.Dispose(); } catch { }
        try { _recvArgs.Dispose(); } catch { }

        _sendQueue.Writer.TryComplete();
        Disconnected?.Invoke(this, ex);
    }

    public void Dispose()
    {
        Close();
        if (_recvArgs.Buffer != null)
            ArrayPool<byte>.Shared.Return(_recvArgs.Buffer);
        _parser.Dispose();
        _cts.Dispose();
    }


    private readonly struct SendBuffer
    {
        public readonly byte[] Buffer;
        public readonly int Length;

        public SendBuffer(byte[] buffer, int length)
        {
            Buffer = buffer;
            Length = length;
        }
    }
}
