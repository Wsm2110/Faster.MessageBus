using System;
using System.Buffers;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Faster.Transport.Ipc
{
    public sealed class IpcListener : IListener
    {
        private readonly string _pipeName;
        private volatile bool _running;
        private Thread? _acceptLoop;

        public event Action<IConnection>? ClientConnected;
        public event Action<IConnection, Exception?>? ClientDisconnected;

        public IpcListener(string pipeName) => _pipeName = pipeName;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _acceptLoop = new Thread(AcceptLoop) { IsBackground = true, Name = $"ipc-listen:{_pipeName}" };
            _acceptLoop.Start();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                                                       PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try
                {
                    server.WaitForConnection();
                    var conn = new IpcConnection(server);
                    conn.Disconnected += (c, ex) => ClientDisconnected?.Invoke(c, ex);
                    ClientConnected?.Invoke(conn);
                }
                catch { try { server.Dispose(); } catch { } }
            }
        }

        public void Stop() => _running = false;
        public void Dispose() => Stop();
    }

    public sealed class IpcClient : IConnection
    {
        private readonly IpcConnection _inner;
        public event Action<IConnection, ReadOnlyMemory<byte>>? OnReceived;
        public event Action<IConnection, Exception?>? Disconnected;

        public IpcClient(string pipeName)
        {
            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect();
            _inner = new IpcConnection(client);
            _inner.OnReceived += (c, m) => OnReceived?.Invoke(this, m);
            _inner.Disconnected += (c, ex) => Disconnected?.Invoke(this, ex);
        }

        public bool TrySend(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) => _inner.TrySend(payload, cancellationToken);
        public void Dispose() => _inner.Dispose();
    }

    internal sealed class IpcConnection : IConnection
    {
        private readonly PipeStream _stream;
        private readonly CancellationTokenSource _cts = new();
        private readonly ManualResetEventSlim _hasFrames = new(false);
        private readonly FrameParserRing _parser = new(1 << 16, 1 << 16);

        public event Action<IConnection, ReadOnlyMemory<byte>>? OnReceived;
        public event Action<IConnection, Exception?>? Disconnected;

        public IpcConnection(PipeStream stream)
        {
            _stream = stream;
            _ = Task.Run(ByteReaderLoop);
            _ = Task.Run(FrameDrainLoop);
        }

        public bool TrySend(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            try
            {
                Span<byte> len = stackalloc byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(len, payload.Length);
                _stream.Write(len);
                _stream.Write(payload.Span);
                _stream.Flush();
                return true;
            }
            catch { return false; }
        }

        private async Task ByteReaderLoop()
        {
            var ct = _cts.Token;
            var buf = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n = await _stream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
                    if (n == 0) break;
                    _parser.Feed(new ReadOnlySpan<byte>(buf, 0, n));
                    _hasFrames.Set();
                }
            }
            catch (Exception ex) { Disconnected?.Invoke(this, ex); }
            finally { ArrayPool<byte>.Shared.Return(buf); _cts.Cancel(); }
        }

        private async Task FrameDrainLoop()
        {
            var ct = _cts.Token;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    _hasFrames.Wait(ct);
                    bool any = false;
                    while (_parser.TryReadFrame(out var frame))
                    {
                        any = true;
                        OnReceived?.Invoke(this, frame);
                    }
                    if (!any) _hasFrames.Reset();
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException) { }
            finally { Disconnected?.Invoke(this, null); }
        }
 
        public void Dispose() { _cts.Cancel(); try { _stream.Dispose(); } catch { } }
    }
}
