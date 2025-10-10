namespace Faster.MessageBus.Features.SocketAsync;

using CommunityToolkit.HighPerformance;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// A framed, thread-safe connection: length-prefixed receive, scatter/gather send.
/// </summary>
public class Connection
{
    private readonly Socket _socket;
    private readonly BufferManager _buffers;
    private readonly SocketAsyncEventArgs _recvArgs;
    private readonly SocketAsyncEventArgs _sendArgs;
    private readonly Channel<Frames> _sendChannel;
    private readonly FrameReader _frameReader = new();
    private readonly CancellationTokenSource _cts = new();

    public event Action<Connection, ReadOnlyMemory<byte>>? OnMessage;
    public event Action<Connection>? OnClosed;

    public Connection(Socket socket, BufferManager buffers, int receiveBufferSize = 8192, int sendQueueCapacity = 1024)
    {
        _socket = socket;
        _buffers = buffers;

        // Receive SAEA
        _recvArgs = new SocketAsyncEventArgs();
        var recvBuffer = _buffers.Rent(receiveBufferSize);
        _recvArgs.SetBuffer(recvBuffer, 0, recvBuffer.Length);
        _recvArgs.Completed += IOCompleted;

        // Send SAEA
        _sendArgs = new SocketAsyncEventArgs();
        _sendArgs.Completed += IOCompleted;

        var opts = new BoundedChannelOptions(sendQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        };
        _sendChannel = Channel.CreateBounded<Frames>(opts);
    }

    public void Start()
    {
        if (!_socket.ReceiveAsync(_recvArgs))
            ProcessReceive(_recvArgs);
        _ = Task.Run(() => SendLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Safe send: copies data into pooled buffer.
    /// </summary>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        // Frame = [length prefix][payload]
        var totalLen = 4 + data.Length;
        var buf = _buffers.Rent(totalLen);
        BitConverter.GetBytes(data.Length).CopyTo(buf, 0);
        data.CopyTo(buf.AsMemory(4, data.Length));

        var work = new Frames();
        work.Segments.Add(new ArraySegment<byte>(buf, 0, totalLen));
        work.RentedHeader = buf;

        await _sendChannel.Writer.WriteAsync(work, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Zero-copy scatter/gather send: header in pooled buffer, body user-supplied.
    /// Caller must keep body valid until send completes!
    /// </summary>
    public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> body, CancellationToken ct = default)
    {
        var headerBuf = _buffers.Rent(4);
        BitConverter.GetBytes(body.Length).CopyTo(headerBuf, 0);

        var work = new Frames();
        work.Segments.Add(new ArraySegment<byte>(headerBuf, 0, 4));
        work.Segments.Add(new ArraySegment<byte>(body.ToArray(), 0, body.Length));
        work.RentedHeader = headerBuf;

        await _sendChannel.Writer.WriteAsync(work, ct).ConfigureAwait(false);
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            var reader = _sendChannel.Reader;
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out var work))
                {
                    _sendArgs.BufferList = work.Segments;
                    _sendArgs.UserToken = work;

                    bool pending;
                    try
                    {
                        pending = _socket.SendAsync(_sendArgs);
                    }
                    catch
                    {
                        work.Completion.TrySetResult(false);
                        CleanupSendWork(work);
                        Close();
                        return;
                    }

                    if (pending)
                        await work.Completion.Task.ConfigureAwait(false);
                    else
                        await work.Completion.Task.ConfigureAwait(false);

                    CleanupSendWork(work);

                    if (!_socket.Connected || ct.IsCancellationRequested)
                        return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }
    }

    private void IOCompleted(object? sender, SocketAsyncEventArgs e)
    {
        switch (e.LastOperation)
        {
            case SocketAsyncOperation.Receive:
                ProcessReceive(e);
                break;
            case SocketAsyncOperation.Send:
                ProcessSend(e);
                break;
        }
    }

    private void ProcessReceive(SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success && e.BytesTransferred > 0)
        {
            var span = new ReadOnlySpan<byte>(e.Buffer!, e.Offset, e.BytesTransferred);
            var frames = _frameReader.Feed(span);
            foreach (var f in frames)
                OnMessage?.Invoke(this, f);

            if (!_socket.ReceiveAsync(e))
                ProcessReceive(e);
        }
        else
        {
            Close();
        }
    }

    private void ProcessSend(SocketAsyncEventArgs e)
    {
        if (e.UserToken is Frames work)
        {
            bool ok = e.SocketError == SocketError.Success;
            work.Completion.TrySetResult(ok);
            e.UserToken = null;
            _sendArgs.BufferList = null;
            if (!ok)
            {
                Close();
            }
        }
    }

    private void CleanupSendWork(Frames work)
    {
        if (work.RentedHeader != null)
        {
            _buffers.Return(work.RentedHeader);
            work.RentedHeader = null;
        }
    }

    public void Close()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
        try { _socket.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket.Close(); } catch { }
        _sendChannel.Writer.TryComplete();

        var recvBuf = _recvArgs.Buffer;
        _recvArgs.SetBuffer(null, 0, 0);
        if (recvBuf != null)
            _buffers.Return(recvBuf);

        OnClosed?.Invoke(this);
    }
}