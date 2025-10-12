namespace Faster.Transport
{
    /// <summary>
    /// Represents a low-level transport connection (TCP, UDP, IPC, or in-process).
    /// </summary>
    /// <remarks>
    /// Implementations of this interface provide asynchronous, lock-free sending and receiving.
    /// The connection is expected to raise <see cref="OnReceived"/> whenever a complete message or datagram is received.
    /// </remarks>
    public interface IConnection : IDisposable
    {
        /// <summary>
        /// Triggered when a complete message or datagram is received.
        /// </summary>
        event Action<IConnection, ReadOnlyMemory<byte>>? OnReceived;

        /// <summary>
        /// Triggered when the connection is closed or an error occurs.
        /// </summary>
        event Action<IConnection, Exception?>? Disconnected;

        /// <summary>
        /// Attempts to send a message asynchronously in a thread-safe manner.
        /// </summary>
        /// <param name="payload">The message payload to send.</param>
        /// <param name="cancellationToken">
        /// Optional token that can be used to cancel the send operation before the message is queued.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the message was successfully enqueued for sending;
        /// otherwise <see langword="false"/>.
        /// </returns>
        bool TrySend(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a transport listener (e.g. TCP listener, UDP server, IPC host).
    /// </summary>
    /// <remarks>
    /// A listener waits for incoming connections or datagrams and raises <see cref="ClientConnected"/>
    /// whenever a new client connects or sends data.
    /// </remarks>
    public interface IListener : IDisposable
    {
        /// <summary>
        /// Triggered when a new client connection is established.
        /// </summary>
        event Action<IConnection>? ClientConnected;

        /// <summary>
        /// Triggered when a connected client disconnects or an error occurs.
        /// </summary>
        event Action<IConnection, Exception?>? ClientDisconnected;

        /// <summary>
        /// Starts accepting incoming connections or messages.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops accepting new connections or messages and closes active ones.
        /// </summary>
        void Stop();
    }

    /// <summary>
    /// Defines supported transport types for the system.
    /// </summary>
    public enum TransportScheme
    {
        /// <summary>In-process transport (shared memory or direct message passing).</summary>
        Inproc,

        /// <summary>Interprocess transport (named pipes or Unix domain sockets).</summary>
        Ipc,

        /// <summary>Transmission Control Protocol (stream-oriented).</summary>
        Tcp,

        /// <summary>User Datagram Protocol (datagram-oriented).</summary>
        Udp
    }

    /// <summary>
    /// Represents a parsed transport endpoint (scheme, host, and port).
    /// </summary>
    /// <remarks>
    /// Endpoints are parsed from URIs such as:
    /// <code>
    /// tcp://127.0.0.1:5000
    /// udp://239.0.0.1:9000
    /// ipc://my_socket
    /// inproc://localbus
    /// </code>
    /// </remarks>
    public readonly struct TransportEndpoint
    {
        /// <summary>
        /// Gets the transport scheme (e.g. TCP, UDP, IPC, or Inproc).
        /// </summary>
        public TransportScheme Scheme { get; }

        /// <summary>
        /// Gets the host name, IP address, or IPC path.
        /// </summary>
        public string HostOrName { get; }

        /// <summary>
        /// Gets the port number associated with the endpoint.
        /// May be zero for non-port-based transports (IPC/Inproc).
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Creates a new <see cref="TransportEndpoint"/> instance.
        /// </summary>
        private TransportEndpoint(TransportScheme scheme, string hostOrName, int port)
        {
            Scheme = scheme;
            HostOrName = hostOrName;
            Port = port;
        }

        /// <summary>
        /// Parses a transport URI into a <see cref="TransportEndpoint"/> structure.
        /// </summary>
        /// <param name="uri">A URI string such as <c>tcp://127.0.0.1:8080</c> or <c>udp://224.1.1.1:9999</c>.</param>
        /// <returns>A parsed <see cref="TransportEndpoint"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="uri"/> is null.</exception>
        /// <exception cref="NotSupportedException">Thrown if the scheme is not supported.</exception>
        public static TransportEndpoint Parse(string uri)
        {
            if (uri is null)
                throw new ArgumentNullException(nameof(uri));

            var u = new Uri(uri, UriKind.Absolute);

            return u.Scheme.ToLowerInvariant() switch
            {
                "inproc" => new TransportEndpoint(TransportScheme.Inproc, u.Host, 0),
                "ipc" => new TransportEndpoint(TransportScheme.Ipc, u.Host, 0),
                "tcp" => new TransportEndpoint(TransportScheme.Tcp, u.Host, u.Port),
                "udp" => new TransportEndpoint(TransportScheme.Udp, u.Host, u.Port),
                _ => throw new NotSupportedException($"Unsupported scheme '{u.Scheme}'.")
            };
        }

        /// <summary>
        /// Returns a string representation of the endpoint.
        /// </summary>
        public override string ToString() =>
            $"{Scheme.ToString().ToLowerInvariant()}://{HostOrName}{(Port > 0 ? $":{Port}" : string.Empty)}";
    }
}
