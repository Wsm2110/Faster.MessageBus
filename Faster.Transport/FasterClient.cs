using Faster.Transport.Primitives;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Faster.Transport;

/// <summary>
/// High-performance framed TCP client using <see cref="SocketAsyncEventArgs"/> and a lock-free MPSC queue for sending.
/// </summary>
/// <remarks>
/// Each message is sent with a 4-byte little-endian length prefix followed by the payload.
/// The class is optimized for ultra-low-latency systems (e.g., trading, telemetry, or gaming) with multiple producer threads.
/// </remarks>
public sealed class FasterClient : IConnection, IDisposable
{
    private readonly Socket _socket;
    private readonly SocketAsyncEventArgs _sendArgs;
    private readonly SocketAsyncEventArgs _recvArgs;
    private readonly FrameParserRing _parser;
    private readonly MpscQueue<SendBuffer> _sendQueue;
    private readonly CancellationTokenSource _cts = new();

    private int _sending;
    private volatile bool _isDisposed;

    private const int DefaultQueueCap = 4096;       // Queue capacity for send buffers

    /// <summary>
    /// Triggered when a complete frame is received.
    /// </summary>
    public event Action<IConnection, ReadOnlyMemory<byte>>? OnReceived;

    /// <summary>
    /// Triggered when the socket disconnects or an error occurs.
    /// </summary>
    public event Action<IConnection, Exception?>? Disconnected;

    /// <summary>
    /// Creates a new <see cref="FasterClient"/> and connects synchronously to the specified endpoint.
    /// </summary>
    public FasterClient(EndPoint remoteEndPoint, int recvBufferSize = 8192, int frameBufferSize = 65536)
        : this(CreateAndConnect(remoteEndPoint), recvBufferSize, frameBufferSize)
    {
    }

    /// <summary>
    /// Creates a new <see cref="FasterClient"/> using an already connected <see cref="Socket"/>.
    /// </summary>
    public FasterClient(Socket connectedSocket, int recvBufferSize = 8192, int frameBufferSize = 65536)
    {
        _socket = connectedSocket ?? throw new ArgumentNullException(nameof(connectedSocket));
        _socket.NoDelay = true;
        _socket.ReceiveBufferSize = 1024 * 1024;
        _socket.SendBufferSize = 1024 * 1024;

        _parser = new(frameBufferSize);
        _sendQueue = new MpscQueue<SendBuffer>(DefaultQueueCap);

        // Setup SocketAsyncEventArgs
        _sendArgs = new SocketAsyncEventArgs();
        _sendArgs.Completed += IOCompleted;

        _recvArgs = new SocketAsyncEventArgs();
        _recvArgs.Completed += IOCompleted;
        _recvArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(recvBufferSize), 0, recvBufferSize);

        // Start send and receive processing
        _ = Task.Run(SendLoop);
        _ = Task.Run(ReceiveLoop);
        StartReceive();
    }

    /// <summary>
    /// Creates and connects a new <see cref="Socket"/> to the provided endpoint.
    /// </summary>
    private static Socket CreateAndConnect(EndPoint ep)
    {
        var s = new Socket(ep.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        s.Connect(ep);
        s.NoDelay = true;
        return s;
    }

    /// <summary>
    /// Attempts to send a message asynchronously (fire-and-forget).
    /// Uses a lock-free MPSC queue to safely enqueue from multiple threads.
    /// </summary>
    /// <remarks>
    /// If the queue is full, the producer thread will perform bounded spinning until a slot frees up.
    /// </remarks>
    public bool TrySend(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return false;
        }

        var buf = ArrayPool<byte>.Shared.Rent(payload.Length + 4);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), payload.Length);
        payload.CopyTo(buf.AsMemory(4));

        var sb = new SendBuffer(buf, payload.Length + 4);

        var spinner = new SpinWait();

        // spin with cooperative cancel
        while (!_sendQueue.TryEnqueue(sb))
        {
            if (_isDisposed || cancellationToken.IsCancellationRequested)
            {
                ArrayPool<byte>.Shared.Return(buf);
                return false;
            }

            spinner.SpinOnce();
        }

        return true;
    }

    /// <summary>
    /// Background loop that continuously dequeues and transmits frames.
    /// Single-consumer thread — no locking required.
    /// </summary>
    private void SendLoop()
    {
        var spinner = new SpinWait();

        while (!_cts.IsCancellationRequested)
        {
            // Wait for a message
            if (!_sendQueue.TryDequeue(out var sb))
            {
                spinner.SpinOnce();
                continue;
            }

            // Wait until send slot becomes free
            while (!SetSending())
            {
                Thread.SpinWait(1);
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
            catch
            {
                ArrayPool<byte>.Shared.Return(sb.Buffer);
                ResetSending();
                break;
            }
        }
    }

    private void ReceiveLoop()
    {
        var spinner = new SpinWait();

        while (!_cts.IsCancellationRequested)
        {
            // Wait for a message
            if (_parser.TryReadFrame(out var frame))
            {
                DispatchFrame(this, frame); // decoupled + safe copy 
                continue;
            }

            spinner.SpinOnce();
        }
    }

    private void DispatchFrame(FasterClient client, ReadOnlyMemory<byte> frame)
    {
        var handler = OnReceived;
        if (handler is null) return;

        // rent a buffer to ensure the memory stays valid off-thread
        var len = frame.Length;
        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(len);
        frame.Span.CopyTo(rented);

        // hop to the ThreadPool to prevent running user code on the networking/frame thread
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var (cli, buf, length, h) = state;
            try
            {
                h(cli, new ReadOnlyMemory<byte>(buf, 0, length));
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buf);
            }
        }, (client, rented, len, handler), preferLocal: false);
    }

    /// <summary>
    /// Handles send completion (both sync and async).
    /// </summary>
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

        ResetSending();
    }

    /// <summary>
    /// Starts asynchronous receiving.
    /// </summary>
    private void StartReceive()
    {
        if (!_socket.ReceiveAsync(_recvArgs))
        {
            ProcessReceive(_recvArgs);
        }
    }

    /// <summary>
    /// Handles incoming data and passes complete frames to the parser.
    /// </summary>
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
            _parser.Feed(data);
            if (!_socket.ReceiveAsync(e))
            {
                ProcessReceive(e);
            }
        }
        catch (Exception ex)
        {
            Close(ex);
            return;
        }
    }

    /// <summary>
    /// Unified I/O completion callback.
    /// </summary>
    private void IOCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (e.LastOperation == SocketAsyncOperation.Send)
        {
            ProcessSend(e);
        }
        else if (e.LastOperation == SocketAsyncOperation.Receive)
        {
            ProcessReceive(e);
        }
    }

    /// <summary>
    /// Atomically marks the send operation as active.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool SetSending()
    {
        return Interlocked.CompareExchange(ref _sending, 1, 0) == 0;
    }

    /// <summary>
    /// Resets the send operation flag, allowing another send to start.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetSending()
    {
        Interlocked.Exchange(ref _sending, 0);
    }

    /// <summary>
    /// Closes the client and cleans up resources.
    /// </summary>
    private void Close(Exception? ex = null)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try { _cts.Cancel(); } catch { }
        try { _socket?.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket?.Dispose(); } catch { }
        try { _sendArgs.Dispose(); } catch { }
        try { _recvArgs.Dispose(); } catch { }

        Disconnected?.Invoke(this, ex);
    }

    /// <summary>
    /// Disposes the client and all its resources.
    /// </summary>
    public void Dispose()
    {
        Close();

        if (_recvArgs.Buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_recvArgs.Buffer);
        }

        _parser.Dispose();
        _cts.Dispose();
    }

    /// <summary>
    /// Represents a pooled send buffer structure.
    /// </summary>
    private readonly struct SendBuffer
    {
        public readonly byte[] Buffer;
        public readonly int Length;

        public SendBuffer(byte[] b, int len)
        {
            Buffer = b;
            Length = len;
        }
    }
}