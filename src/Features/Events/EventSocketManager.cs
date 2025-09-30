using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;
using NetMQ.Sockets;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Manages the lifecycle of NetMQ sockets for event-based communication.
/// It dynamically creates and destroys connections to other nodes by listening to mesh membership changes.
/// All socket operations are executed on a dedicated scheduler thread to guarantee thread safety without locks.
/// </summary>
internal class EventSocketManager : IEventSocketManager, IDisposable
{
    /// <summary>
    /// A thread-safe list of active subscriber sockets, keyed by the unique ID of the mesh node they connect to.
    /// </summary>
    private readonly List<(ulong Id, SubscriberSocket Socket)> _socketInfoList = new();

    /// <summary>
    /// Used for subscribing to and unsubscribing from mesh membership changes.
    /// </summary>
    private readonly IEventAggregator _eventAggregator;

    /// <summary>
    /// Application-level configuration options.
    /// </summary>
    private readonly IOptions<MessageBrokerOptions> _options;

    /// <summary>
    /// The dedicated thread scheduler that ensures all socket operations are thread-safe.
    /// </summary>
    private readonly IEventScheduler _scheduler;

    /// <summary>
    /// Provides topic information for socket subscriptions.
    /// </summary>
    private readonly IEventHandlerProvider _eventHandlerProvider;

    /// <summary>
    /// The handler that processes incoming messages from subscriber sockets.
    /// </summary>
    private readonly IEventReceivedHandler _eventReceivedHandler;

    /// <summary>
    /// Pre-created delegates for subscribing to mesh events, stored for later unsubscription during disposal.
    /// </summary>
    private readonly Action<MeshJoined> _onMeshJoined;
    private readonly Action<MeshRemoved> _onMeshRemoved;

    private bool _disposed;

    /// <summary>
    /// Gets the number of active subscriber sockets currently managed.
    /// </summary>
    public int Count => _socketInfoList.Count;

    /// <summary>
    /// Gets the single, outbound publisher socket used by this node to broadcast its own events.
    /// </summary>
    public PublisherSocket PublisherSocket { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSocketManager"/> class.
    /// This constructor sets up the node's own publisher socket and subscribes to mesh events
    /// to manage connections to other nodes.
    /// </summary>
    /// <param name="endpoint">An object that will be updated with the dynamically chosen _port for publishing.</param>
    /// <param name="eventAggregator">The event aggregator for mesh lifecycle events.</param>
    /// <param name="eventScheduler">The scheduler for thread-safe socket operations.</param>
    /// <param name="eventHandlerProvider">The provider for registered event topics.</param>
    /// <param name="eventReceivedHandler">The handler for incoming event messages.</param>
    /// <param name="options">The application configuration options.</param>
    public EventSocketManager(
        MeshApplication endpoint,
        IEventAggregator eventAggregator,
        IEventScheduler eventScheduler,
        IEventHandlerProvider eventHandlerProvider,
        IEventReceivedHandler eventReceivedHandler,
        IOptions<MessageBrokerOptions> options)
    {
        _eventAggregator = eventAggregator;
        _options = options;
        _scheduler = eventScheduler;
        _eventHandlerProvider = eventHandlerProvider;
        _eventReceivedHandler = eventReceivedHandler;

        // Store delegates in fields so they can be referenced for unsubscription in Dispose().
        _onMeshJoined = data => AddSocket(data.Info);
        _onMeshRemoved = data => RemoveSocket(data.Info);

        // Subscribe to events that announce when other nodes join or leave the mesh.
        eventAggregator.Subscribe(_onMeshJoined);
        eventAggregator.Subscribe(_onMeshRemoved);

        // Create and configure the single Publisher socket for this node.
        PublisherSocket = new PublisherSocket();
        PublisherSocket.Options.Linger = TimeSpan.Zero;           // Discard unsent messages on close
        PublisherSocket.Options.SendHighWatermark = 1_000_000;    // Allow a large buffer of outgoing messages
        PublisherSocket.Options.ReceiveHighWatermark = 1_000_000; // Allow a large buffer of incoming messages
        PublisherSocket.Options.Backlog = 1024;                   // Connection queue size for bursty connects
        PublisherSocket.Options.TcpKeepalive = true;
        PublisherSocket.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(30);
        PublisherSocket.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(10);
        PublisherSocket.Options.ReceiveBuffer = 1024 * 1024;      // Set OS receive buffer size to 1MB
        PublisherSocket.Options.SendBuffer = 1024 * 1024;         // Set OS send buffer size to 1MB

        // Find an available TCP _port and bind the publisher socket to it.
        var port = PortFinder.BindPort(options.Value.PublishPort, (ushort)(options.Value.PublishPort + 200), port => PublisherSocket.Bind($"tcp://*:{port}"));

        Console.WriteLine($"Publisher socket bound to tcp://*:{port}");

        // Update the shared endpoint object so other parts of the application know which _port was chosen.
        endpoint.PubPort = (ushort)port;
    }

    /// <summary>
    /// Creates and configures a new <see cref="SubscriberSocket"/> to connect to a remote node.
    /// This entire operation is scheduled on a dedicated thread to ensure thread safety.
    /// </summary>
    /// <param name="info">The mesh node information containing the address and _port to connect to.</param>
    public void AddSocket(MeshContext info)
    {
        // Offload socket creation and configuration to the scheduler's thread.
        _scheduler.Invoke(poller =>
        {
            var subSocket = new SubscriberSocket();

            // Hook the message received handler to process incoming events.
            subSocket.ReceiveReady += _eventReceivedHandler.OnEventReceived;

            // Connect to the remote node’s PUB Socket.
            subSocket.Connect($"tcp://{info.Address}:{info.PubPort}");

            // Subscribe to all topics. An empty string is NetMQ's wildcard for all topics.
            subSocket.Subscribe("");

            _socketInfoList.Add((info.MeshId, subSocket));

            // TryAdd the newly created socket to the poller to begin receiving messages.
            poller.Add(subSocket);
        });
    }

    /// <summary>
    /// Finds, removes, and disposes the <see cref="SubscriberSocket"/> for a specified mesh node.
    /// This cleanup operation is scheduled on a dedicated thread for safety.
    /// </summary>
    /// <param name="meshInfo">The mesh node identifying which socket to remove.</param>
    public void RemoveSocket(MeshContext meshInfo)
    {
        // Offload socket removal to the scheduler's thread.
        _scheduler.Invoke(poller =>
        {
            for (int i = 0; i < _socketInfoList.Count; i++)
            {
                var socketInfo = _socketInfoList[i];

                if (socketInfo.Id == meshInfo.MeshId)
                {
                    // TryRemove the socket from the poller first to stop receiving events.
                    poller.Remove(socketInfo.Socket);

                    // Unsubscribe event handlers and dispose of the socket resources.
                    CleanupSocket(socketInfo.Socket);

                    // Finally, remove the socket from the managed list.
                    _socketInfoList.RemoveAt(i);
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Disposes this manager and all associated sockets and resources.
    /// Ensures proper event unsubscription and thread-safe cleanup of all network resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop listening for any new nodes joining or leaving.
        _eventAggregator.Unsubscribe(_onMeshJoined);
        _eventAggregator.Unsubscribe(_onMeshRemoved);

        PublisherSocket.Dispose();

        // Schedule the final cleanup of all active sockets on the scheduler thread.
        _scheduler.Invoke(poller =>
        {
            foreach (var socketinfo in _socketInfoList)
            {
                poller.Remove(socketinfo.Socket);
                CleanupSocket(socketinfo.Socket);
            }
            _socketInfoList.Clear();
        });       

        // Dispose the scheduler itself, which will stop its dedicated thread.   
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Safely unsubscribes event handlers and disposes a <see cref="SubscriberSocket"/>.
    /// This is a private helper that must always be called on the scheduler's thread.
    /// </summary>
    /// <param name="socket">The socket to clean up.</param>
    private void CleanupSocket(SubscriberSocket socket)
    {
        socket.ReceiveReady -= _eventReceivedHandler.OnEventReceived!;
        socket.Dispose();
    }
}