using Faster.Transport;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A high-performance asynchronous TCP server that accepts multiple clients
/// and wraps each connection in a <see cref="FasterClient"/> for framed message I/O.
/// </summary>
/// <remarks>
/// Uses <see cref="SocketAsyncEventArgs"/> for non-blocking accepts
/// and a thread-safe client registry based on <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// Designed for zero-allocation operation, linear scalability, and clean resource disposal.
/// </remarks>
public sealed class FasterServer : IDisposable
{
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, FasterClient> _clients = new();
    private SocketAsyncEventArgs? _acceptArgs;
    private int _clientCounter;
    private bool _isRunning;

    /// <summary>
    /// Occurs when a new client successfully connects to the server.
    /// </summary>
    public event Action<FasterClient>? ClientConnected;

    /// <summary>
    /// Occurs when a connected client disconnects or encounters an error.
    /// </summary>
    public event Action<FasterClient, Exception?>? ClientDisconnected;

    /// <summary>
    /// Occurs when a complete framed message is received from any connected client.
    /// </summary>
    public event Action<FasterClient, ReadOnlyMemory<byte>>? OnReceived;

    /// <summary>
    /// Initializes and binds a new <see cref="FasterServer"/> instance to the specified endpoint.
    /// </summary>
    /// <param name="bindEndPoint">The <see cref="EndPoint"/> to bind and listen on.</param>
    /// <param name="backlog">The maximum number of pending connection requests (default = 100).</param>
    public FasterServer(EndPoint bindEndPoint, int backlog = 100)
    {
        _listener = new Socket(bindEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _listener.NoDelay = true; // Disable Nagle's algorithm for low-latency  
        _listener.Bind(bindEndPoint);
        _listener.Listen(backlog);
    }

    /// <summary>
    /// Starts the server and begins asynchronously accepting incoming client connections.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the server is already running.</exception>
    public void Start()
    {
        if (_isRunning)
            throw new InvalidOperationException("Server is already running.");

        _isRunning = true;

        // Initialize a single reusable accept args instance for all async accept operations.
        _acceptArgs = new SocketAsyncEventArgs();
        _acceptArgs.Completed += AcceptCompleted;

        // Begin the accept loop.
        AcceptNext(_acceptArgs);
    }

    /// <summary>
    /// Broadcasts a single message payload to all currently connected clients.
    /// </summary>
    /// <param name="payload">The binary payload to send.</param>
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
                // Ignore transient send errors — clients clean up on disconnect.
            }
        }
    }

    /// <summary>
    /// Disconnects and disposes a specific client by reference.
    /// </summary>
    /// <param name="client">The target <see cref="FasterClient"/> instance to disconnect.</param>
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
    /// Initiates the next asynchronous accept operation.
    /// This pattern reuses a single <see cref="SocketAsyncEventArgs"/> object for efficiency.
    /// </summary>
    private void AcceptNext(SocketAsyncEventArgs e)
    {
        if (!_isRunning || _cts.IsCancellationRequested)
        {
            e.Dispose();
            return;
        }

        // Reset for next accept
        e.AcceptSocket = null;

        try
        {
            // Begin async accept; if completed synchronously, handle immediately.
            bool pending = _listener.AcceptAsync(e);
            if (!pending)
                ProcessAccept(e);
        }
        catch (ObjectDisposedException)
        {
            // Listener closed while awaiting accept — safe to exit.
            e.Dispose();
        }
        catch (Exception ex)
        {
            // Log and retry on transient errors.
            if (_isRunning)
            {
                Console.Error.WriteLine($"[FasterServer] AcceptNext failed: {ex.Message}");
                _ = Task.Delay(100, _cts.Token)
                    .ContinueWith(t =>
                    {
                        if (!t.IsCanceled)
                            AcceptNext(e);
                    }, TaskScheduler.Default);
            }
            else
            {
                e.Dispose();
            }
        }
    }

    /// <summary>
    /// Callback invoked when an asynchronous accept completes.
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
    /// Handles a completed accept operation and wires up the new client connection.
    /// </summary>
    /// <param name="e">The <see cref="SocketAsyncEventArgs"/> holding the accepted socket.</param>
    private void ProcessAccept(SocketAsyncEventArgs e)
    {
        if (!_isRunning || _cts.IsCancellationRequested)
        {
            e.Dispose();
            return;
        }

        // Check for socket errors
        if (e.SocketError != SocketError.Success || e.AcceptSocket == null)
        {
            try { e.AcceptSocket?.Close(); } catch { }
            AcceptNext(e);
            return;
        }

        var socket = e.AcceptSocket;
        var clientId = Interlocked.Increment(ref _clientCounter);
        var client = new FasterClient(socket);

        // Register client and wire up event forwarding.
        _clients[clientId] = client;

        client.OnReceived += (c, frame) => OnReceived?.Invoke(c, frame);

        client.Disconnected += (c, ex) =>
        {
            _clients.TryRemove(clientId, out _);
            ClientDisconnected?.Invoke(c, ex);
        };

        ClientConnected?.Invoke(client);

        // Queue the next accept immediately.
        AcceptNext(e);
    }

    /// <summary>
    /// Stops the server, closes the listener, and disposes all connected clients.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cts.Cancel();

        try { _listener.Close(); } catch { }
        try { _acceptArgs?.Dispose(); } catch { }

        // Dispose all connected clients to release sockets and buffers.
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
