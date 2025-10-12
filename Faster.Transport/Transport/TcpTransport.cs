using System;
using System.Net;

namespace Faster.Transport.Tcp
{
    public sealed class TcpListenerAdapter : IListener
    {
        private readonly FasterServer _server;

        public event Action<IConnection>? ClientConnected;
        public event Action<IConnection, Exception?>? ClientDisconnected;

        public TcpListenerAdapter(EndPoint bind)
        {
            _server = new FasterServer(bind);
            _server.ClientConnected += OnServerClientConnected;
            _server.ClientDisconnected += (cli, ex) => { };
        }

        private void OnServerClientConnected(IConnection scli)
        {
            var conn = (IConnection)scli;
            scli.Disconnected += (_, ex) => ClientDisconnected?.Invoke(conn, ex);
            ClientConnected?.Invoke(conn);
        }

        public void Start() => _server.Start();
        public void Stop()  => _server.Stop();
        public void Dispose() => _server.Dispose();
    }

    public sealed class TcpClientAdapter : IConnection
    {
        private readonly FasterClient _client;
        public event Action<IConnection, ReadOnlyMemory<byte>>? OnReceived;
        public event Action<IConnection, Exception?>? Disconnected;

        public TcpClientAdapter(EndPoint remote)
        {
            _client = new FasterClient(remote);
            _client.OnReceived += (_, f) => OnReceived?.Invoke(this, f);
            _client.Disconnected += (_, ex) => Disconnected?.Invoke(this, ex);
        }

        public bool TrySend(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) => _client.TrySend(payload, cancellationToken);
  
        public void Dispose() => _client.Dispose();
    }
}
