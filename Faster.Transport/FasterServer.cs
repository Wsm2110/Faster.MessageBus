using Faster.Transport;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Faster.Transport;

/// <summary>
/// A high-performance asynchronous TCP server that accepts multiple clients
/// and wraps each connection in a <see cref="FasterClient"/> for framed message I/O.
/// </summary>
/// <remarks>
/// Uses <see cref="SocketAsyncEventArgs"/> for non-blocking accepts and a thread-safe
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> to track connected clients.
/// Designed for low latency, zero-allocation data paths, and clean resource disposal.
/// </remarks>
public sealed class FasterServer : IDisposable
{
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, IConnection> _clients = new();
    private SocketAsyncEventArgs? _acceptArgs;
    private int _clientCounter;
    private bool _isRunning;

    /// <summary>
    /// Occurs when a new client successfully connects to the server.
    /// </summary>
    public event Action<IConnection>? ClientConnected;

    /// <summary>
    /// Occurs when a connected client disconnects or encounters an error.
    /// </summary>
    public event Action<IConnection, Exception?>? ClientDisconnected;

    /// <summary>
    /// Occurs when a complete framed message is received from any connected client.
    /// </summary>
    public event Action<IConnection, ReadOnlyMemory<byte>>? OnReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="FasterServer"/> class and binds it to the specified endpoint.
    /// </summary>
    /// <param name="bindEndPoint">The network <see cref="EndPoint"/> to bind and listen on.</param>
    /// <param name="backlog">Maximum number of pending connections (default = 100).</param>
    public FasterServer(EndPoint bindEndPoint, int backlog = 100)
    {
        _listener = new Socket(bindEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true // Disable Nagle's algorithm for low-latency transmission.
        };

        _listener.Bind(bindEndPoint);
        _listener.Listen(backlog);
    }

    /// <summary>
    /// Starts the server and begins asynchronously accepting client connections.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the server is already running.</exception>
    public void Start()
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("Server is already running.");
        }

        _isRunning = true;

        // Initialize a reusable accept event args instance for efficient async accepts.
        _acceptArgs = new SocketAsyncEventArgs();
        _acceptArgs.Completed += AcceptCompleted;

        // Begin the first asynchronous accept operation.
        AcceptNext(_acceptArgs);
    }

    /// <summary>
    /// Broadcasts a message to all connected clients.
    /// </summary>
    /// <param name="payload">The message payload to send to all clients.</param>
    public void Broadcast(ReadOnlyMemory<byte> payload)
    {
        foreach (var client in _clients.Values)
        {
            try
            {
                client.TrySend(payload);
            }
            catch
            {
                // Ignore transient send failures; clients will be removed on disconnect.
            }
        }
    }

    /// <summary>
    /// Disconnects and disposes a specific client.
    /// </summary>
    /// <param name="client">The <see cref="FasterClient"/> instance to disconnect.</param>
    public void DisconnectClient(FasterClient client)
    {
        foreach (var kv in _clients)
        {
            if (kv.Value == client)
            {
                kv.Value.Dispose();
                _clients.TryRemove(kv.Key, out _);
                break;
            }
        }
    }

    /// <summary>
    /// Begins an asynchronous accept operation using a reusable <see cref="SocketAsyncEventArgs"/>.
    /// </summary>
    /// <param name="e">The event args object reused for accept operations.</param>
    private void AcceptNext(SocketAsyncEventArgs e)
    {
        if (!_isRunning || _cts.IsCancellationRequested)
        {
            e.Dispose();
            return;
        }

        // Reset for reuse.
        e.AcceptSocket = null;

        try
        {
            // Start async accept. If it completes synchronously, process immediately.
            bool pending = _listener.AcceptAsync(e);
            if (!pending)
            {
                ProcessAccept(e);
            }
        }
        catch (ObjectDisposedException)
        {
            // Listener closed while waiting; safe to ignore.
            e.Dispose();
        }
        catch (Exception ex)
        {
            // Retry on transient errors.
            if (_isRunning)
            {
                Console.Error.WriteLine($"[FasterServer] AcceptNext failed: {ex.Message}");

                _ = Task.Delay(100, _cts.Token)
                    .ContinueWith(t =>
                    {
                        if (!t.IsCanceled)
                        {
                            AcceptNext(e);
                        }
                    }, TaskScheduler.Default);
            }
            else
            {
                e.Dispose();
            }
        }
    }

    /// <summary>
    /// Handles completion of an asynchronous accept operation.
    /// </summary>
    private void AcceptCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (!_isRunning || _cts.IsCancellationRequested)
        {
            e.Dispose();
            return;
        }

        ProcessAccept(e);
    }

    /// <summary>
    /// Processes a successfully accepted client connection and wires up its events.
    /// </summary>
    /// <param name="e">The <see cref="SocketAsyncEventArgs"/> containing the accepted socket.</param>
    private void ProcessAccept(SocketAsyncEventArgs e)
    {
        if (!_isRunning || _cts.IsCancellationRequested)
        {
            e.Dispose();
            return;
        }

        // Validate socket result.
        if (e.SocketError != SocketError.Success || e.AcceptSocket == null)
        {
            try { e.AcceptSocket?.Close(); } catch { }
            AcceptNext(e);
            return;
        }

        var socket = e.AcceptSocket;
        var clientId = Interlocked.Increment(ref _clientCounter);
        var client = new FasterClient(socket);

        // Register the client in the active collection.
        _clients[clientId] = client;

        // Forward client events to server-level events.
        client.OnReceived += (c, frame) => OnReceived?.Invoke(c, frame);
        client.Disconnected += (c, ex) =>
        {
            _clients.TryRemove(clientId, out _);
            ClientDisconnected?.Invoke(c, ex);
        };

        // Notify subscribers that a new client has connected.
        ClientConnected?.Invoke(client);

        // Immediately queue next accept operation.
        AcceptNext(e);
    }

    /// <summary>
    /// Stops the server, closes the listener socket, and disposes all connected clients.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _cts.Cancel();

        try { _listener.Close(); } catch { }
        try { _acceptArgs?.Dispose(); } catch { }

        // Dispose all connected clients to release resources.
        foreach (var kv in _clients)
        {
            try { kv.Value.Dispose(); } catch { }
        }

        _clients.Clear();
    }

    /// <summary>
    /// Releases all resources used by the server, including sockets,
    /// cancellation tokens, and client connections.
    /// </summary>
    public void Dispose()
    {
        Stop();

        try { _listener.Dispose(); } catch { }
        try { _acceptArgs?.Dispose(); } catch { }
        _cts.Dispose();
    }
}
