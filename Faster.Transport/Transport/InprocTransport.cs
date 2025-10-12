using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

namespace Faster.Transport.Inproc
{
    internal static class InprocBroker
    {
        private static readonly ConcurrentDictionary<string, InprocListener> Map = new();
        public static InprocListener GetOrCreate(string name) => Map.GetOrAdd(name, n => new InprocListener(n));
    }

    public sealed class InprocListener : IListener
    {
        private readonly string _name;
        private volatile bool _running;

        public event Action<IConnection>? ClientConnected;
        public event Action<IConnection, Exception?>? ClientDisconnected;

        internal InprocListener(string name) => _name = name;
        public void Start() => _running = true;
        public void Stop() => _running = false;
        public void Dispose() => Stop();

        internal void AcceptPair(InprocConnection serverSide, InprocConnection clientSide)
        {
            if (!_running) { clientSide.Dispose(); serverSide.Dispose(); return; }
            clientSide.Disconnected += (c, ex) => ClientDisconnected?.Invoke(c, ex);
            serverSide.Disconnected += (c, ex) => ClientDisconnected?.Invoke(c, ex);
            ClientConnected?.Invoke(serverSide);
        }
    }

    public sealed class InprocClient : IConnection
    {
        private readonly InprocConnection _conn;
        public event Action<IConnection, ReadOnlyMemory<byte>>? OnReceived;
        public event Action<IConnection, Exception?>? Disconnected;

        public InprocClient(string name)
        {
            var a2b = new Faster.Transport.Primitives.SpscRingBuffer<(byte[] arr, int len)>(1024);
            var b2a = new Faster.Transport.Primitives.SpscRingBuffer<(byte[] arr, int len)>(1024);

            var serverSide = new InprocConnection(b2a, a2b);
            _conn = new InprocConnection(a2b, b2a);

            var listener = InprocBroker.GetOrCreate(name);
            _conn.OnReceived += (c, m) => OnReceived?.Invoke(this, m);
            _conn.Disconnected += (c, ex) => Disconnected?.Invoke(this, ex);
            listener.AcceptPair(serverSide, _conn);
        }

        public bool TrySend(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) => _conn.TrySend(payload, cancellationToken);

        public void Dispose() => _conn.Dispose();
    }

    internal sealed class InprocConnection : IConnection
    {
        private readonly Faster.Transport.Primitives.SpscRingBuffer<(byte[] arr, int len)> _incoming;
        private readonly Faster.Transport.Primitives.SpscRingBuffer<(byte[] arr, int len)> _outgoing;
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _pump;

        public event Action<IConnection, ReadOnlyMemory<byte>>? OnReceived;
        public event Action<IConnection, Exception?>? Disconnected;

        public InprocConnection(Faster.Transport.Primitives.SpscRingBuffer<(byte[] arr, int len)> incoming,
                                Faster.Transport.Primitives.SpscRingBuffer<(byte[] arr, int len)> outgoing)
        {
            _incoming = incoming;
            _outgoing = outgoing;
            _pump = new Thread(Pump) { IsBackground = true, Priority = ThreadPriority.Highest };
            _pump.Start();
        }

        public bool TrySend(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            var arr = ArrayPool<byte>.Shared.Rent(payload.Length);
            payload.Span.CopyTo(arr);
            if (!_outgoing.TryEnqueue((arr, payload.Length)))
            {
                ArrayPool<byte>.Shared.Return(arr);
                return false;
            }
            return true;
        }

        private void Pump()
        {
            var spin = new SpinWait();
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (_incoming.TryDequeue(out var owned))
                    {
                        try { OnReceived?.Invoke(this, new ReadOnlyMemory<byte>(owned.arr, 0, owned.len)); }
                        finally { ArrayPool<byte>.Shared.Return(owned.arr); }
                    }
                    else spin.SpinOnce();
                }
            }
            finally { Disconnected?.Invoke(this, null); }
        }

        public void Dispose() => _cts.Cancel();
    }
}
