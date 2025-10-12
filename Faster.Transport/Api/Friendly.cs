using System;
using System.Text;
using System.Text.Json;
using Faster.Transport.Serialization;
using Faster.Transport.Transport;

namespace Faster.Transport.Friendly
{
    /// <summary>
    /// Describes the performance configuration for a client or server.
    /// </summary>
    /// <param name="HighPriority">Whether to use high thread priority for send/receive loops.</param>
    /// <param name="HandlerCpu">Optional CPU affinity for the handler thread.</param>
    /// <param name="SenderCpu">Optional CPU affinity for the sender thread.</param>
    /// <param name="InlineHandlers">If true, handle OnMessage callbacks inline on the IO thread (lowest latency).</param>
    public record PerformanceMode(bool HighPriority = true, int? HandlerCpu = null, int? SenderCpu = null, bool InlineHandlers = false);

    /// <summary>
    /// Entry point for the user-facing Friendly API.
    /// Provides static helpers to start servers or connect clients using a unified endpoint syntax.
    /// </summary>
    public static class Net
    {
        /// <summary>
        /// Creates a new <see cref="Server"/> instance that listens on the given endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint string such as "tcp://127.0.0.1:5555" or "ipc://MyPipe".</param>
        /// <param name="serializer">Optional custom serializer for messages.</param>
        /// <param name="perf">Optional performance tuning options.</param>
        public static Server Listen(string endpoint, ISerializer? serializer = null, PerformanceMode? perf = null)
            => new Server(endpoint, serializer, perf);

        /// <summary>
        /// Creates a new <see cref="Client"/> instance that connects to the given endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint string such as "tcp://127.0.0.1:5555" or "udp://10.0.0.5:6000".</param>
        /// <param name="serializer">Optional custom serializer for messages.</param>
        /// <param name="perf">Optional performance tuning options.</param>
        public static Client Connect(string endpoint, ISerializer? serializer = null, PerformanceMode? perf = null)
            => new Client(endpoint, serializer, perf);
    }

    /// <summary>
    /// Represents a high-level friendly server wrapper.
    /// Handles connections, message dispatch, and serialization via the <see cref="ISerializer"/> interface.
    /// </summary>
    public sealed class Server : IAsyncDisposable, IDisposable
    {
        private readonly Faster.Transport.IListener _listener;
        private readonly ISerializer _serializer;
        private readonly PerformanceMode _perf;

        private bool _started;
        private bool _disposed;

        private Action<Connection>? _onConnected;
        private Action<Connection, Exception?>? _onDisconnected;
        private Action<Connection, ReadOnlyMemory<byte>>? _onMessage;

        /// <summary>
        /// Constructs a new server instance that binds to the given endpoint.
        /// </summary>
        public Server(string endpoint, ISerializer? serializer = null, PerformanceMode? perf = null)
        {
            var ep = Faster.Transport.TransportEndpoint.Parse(endpoint);
            _listener = TransportFactory.CreateListener(ep);
            _serializer = serializer ?? new MessagePackNetSerializer();
            _perf = perf ?? new PerformanceMode();

            // Wire low-level listener to friendly event model
            _listener.ClientConnected += conn =>
            {
                var c = new Connection(conn, _serializer);
                if (_onMessage is not null) c.Message += _onMessage;
                if (_onDisconnected is not null) c.Disconnected += _onDisconnected;
                _onConnected?.Invoke(c);
            };
        }

        /// <summary>
        /// Registers a handler to be invoked whenever a new connection is accepted.
        /// </summary>
        public Server OnConnect(Action<Connection> handler) { _onConnected = handler; return this; }

        /// <summary>
        /// Registers a handler to be invoked when a connection disconnects or fails.
        /// </summary>
        public Server OnDisconnect(Action<Connection, Exception?> handler) { _onDisconnected = handler; return this; }

        /// <summary>
        /// Registers a raw message handler (untyped).
        /// </summary>
        public Server OnMessage(Action<Connection, ReadOnlyMemory<byte>> handler) { _onMessage = handler; return this; }

        /// <summary>
        /// Registers a typed message handler using the configured serializer.
        /// </summary>
        /// <typeparam name="T">Message type to deserialize into.</typeparam>
        /// <param name="handler">Handler invoked when a message of type <typeparamref name="T"/> is received.</param>
        /// <param name="onError">Optional handler invoked when deserialization fails.</param>
        public Server On<T>(Action<Connection, T> handler, Action<Connection, Exception, ReadOnlyMemory<byte>>? onError = null)
        {
            var prev = _onMessage;
            _onMessage = (conn, bytes) =>
            {
                try
                {
                    handler(conn, conn.Deserialize<T>(bytes));
                }
                catch (Exception ex)
                {
                    if (onError is not null)
                        onError(conn, ex, bytes);
                    else
                        prev?.Invoke(conn, bytes);
                }
            };
            return this;
        }

        /// <summary>
        /// Starts listening for incoming connections.
        /// </summary>
        public Server Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Server));
            if (_started) return this;

            _listener.Start();
            _started = true;
            return this;
        }

        /// <summary>
        /// Stops listening for connections.
        /// </summary>
        public Server Stop()
        {
            if (!_started) return this;

            _listener.Stop();
            _started = false;
            return this;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            (_listener as IDisposable)?.Dispose();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    /// <summary>
    /// Represents a friendly client connection that abstracts transport details (TCP, UDP, IPC).
    /// </summary>
    public sealed class Client : IAsyncDisposable, IDisposable
    {
        private readonly Connection _conn;
        private readonly PerformanceMode _perf;
        private bool _disposed;

        /// <summary>
        /// Connects to a remote endpoint using the specified serializer and performance configuration.
        /// </summary>
        public Client(string endpoint, ISerializer? serializer = null, PerformanceMode? perf = null)
        {
            var ep = Faster.Transport.TransportEndpoint.Parse(endpoint);
            var inner = TransportFactory.CreateClient(ep);
            _perf = perf ?? new PerformanceMode();
            _conn = new Connection(inner, serializer ?? new MessagePackNetSerializer());
        }

        /// <summary>
        /// Registers a raw message handler (untyped).
        /// </summary>
        public Client OnMessage(Action<Connection, ReadOnlyMemory<byte>> handler) { _conn.Message += handler; return this; }

        /// <summary>
        /// Registers a disconnect handler.
        /// </summary>
        public Client OnDisconnect(Action<Connection, Exception?> handler) { _conn.Disconnected += handler; return this; }

        /// <summary>
        /// Registers a typed message handler using the configured serializer.
        /// </summary>
        public Client On<T>(Action<Connection, T> handler, Action<Connection, Exception, ReadOnlyMemory<byte>>? onError = null)
        {
            _conn.Message += (c, bytes) =>
            {
                try { handler(c, c.Deserialize<T>(bytes)); }
                catch (Exception ex) { onError?.Invoke(c, ex, bytes); }
            };
            return this;
        }

        /// <summary>Sends a raw binary payload.</summary>
        public bool Send(ReadOnlyMemory<byte> payload) => _conn.Send(payload);

        /// <summary>Sends a UTF-8 encoded string payload.</summary>
        public bool SendString(string text) => _conn.Send(Encoding.UTF8.GetBytes(text));

        /// <summary>Sends an object serialized to JSON using the configured serializer.</summary>
        public bool SendJson<T>(T value, JsonSerializerOptions? _ = null) => _conn.Send(_conn.Serializer.Serialize(value));

        /// <summary>Sends a strongly-typed message using the configured serializer.</summary>
        public bool Send<T>(T value) => _conn.Send(_conn.Serializer.Serialize(value));

        /// <summary>Sends a strongly-typed message using a custom serializer.</summary>
        public bool Send<T>(T value, ISerializer serializer) => _conn.Send(serializer.Serialize(value));

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _conn.Dispose();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    /// <summary>
    /// Represents a high-level logical connection, wrapping an <see cref="IConnection"/> transport.
    /// Provides event-based message and disconnect notifications.
    /// </summary>
    public sealed class Connection : IDisposable
    {
        private readonly Faster.Transport.IConnection _inner;
        internal ISerializer Serializer { get; }
        private bool _disposed;

        internal Connection(Faster.Transport.IConnection inner, ISerializer serializer)
        {
            _inner = inner;
            Serializer = serializer;

            // Wire low-level transport to friendly events
            _inner.Disconnected += (_, ex) => Disconnected?.Invoke(this, ex);
            _inner.OnReceived += (_, mem) => Message?.Invoke(this, mem);
        }

        /// <summary>Fired when a message is received from the remote endpoint.</summary>
        public event Action<Connection, ReadOnlyMemory<byte>>? Message;

        /// <summary>Fired when the underlying transport disconnects or fails.</summary>
        public event Action<Connection, Exception?>? Disconnected;

        /// <summary>Sends a raw payload through the transport.</summary>
        public bool Send(ReadOnlyMemory<byte> payload) => _inner.TrySend(payload);
           
        /// <summary>Deserializes a received byte span into a strongly-typed object.</summary>
        internal T Deserialize<T>(ReadOnlyMemory<byte> data) => Serializer.Deserialize<T>(data);


        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            (_inner as IDisposable)?.Dispose();
        }
    }
}
