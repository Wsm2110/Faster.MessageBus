using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;
using NetMQ.Sockets;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Manages a collection of <see cref="DealerSocket"/> instances for communication 
/// with other nodes on the same machine. 
/// 
/// All Socket operations are executed on a dedicated scheduler thread 
/// to guarantee thread safety without locks.
/// </summary>
internal class EventSocketManager : IEventSocketManager, IDisposable
{
    /// <summary>
    /// Internal list of sockets keyed by MeshInfo.Id.
    /// </summary>
    private readonly List<(string Id, SubscriberSocket Socket)> _socketInfoList = new();


    /// <summary>
    /// Event aggregator for subscribing to mesh membership changes.
    /// </summary>
    private readonly IEventAggregator _eventAggregator;
    private readonly IOptions<MessageBrokerOptions> _options;
    private readonly IEventScheduler _scheduler;
    private readonly IEventHandlerProvider _eventHandlerProvider;
    private readonly IEventReceivedHandler _eventReceivedHandler;
    private readonly IEventScheduler _eventScheduler;

    /// <summary>
    /// Delegates stored for unsubscribing from mesh join/remove events during disposal.
    /// </summary>
    private readonly Action<MeshJoined> _onMeshJoined;
    private readonly Action<MeshRemoved> _onMeshRemoved;

    private bool _disposed;

    /// <summary>
    /// Gets the number of sockets currently managed.
    /// </summary>
    public int Count => _socketInfoList.Count;

    /// <summary>
    /// 
    /// </summary>
    public PublisherSocket PublisherSocket { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="MachineSocketManager"/>.
    /// Subscribes to mesh events and sets up the scheduler context.
    /// </summary>
    public EventSocketManager(
        LocalEndpoint endpoint,
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

        // Store delegates for later unsubscription.
        _onMeshJoined = data => AddSocket(data.Info);
        _onMeshRemoved = data => RemoveSocket(data.Info);

        // Subscribe to cluster mesh lifecycle events.
        eventAggregator.Subscribe(_onMeshJoined);
        eventAggregator.Subscribe(_onMeshRemoved);

        PublisherSocket = new PublisherSocket();
        PublisherSocket.Options.Linger = TimeSpan.Zero;             // Don't buffer on close
        PublisherSocket.Options.SendHighWatermark = 1_000_000;      // Huge outbound queue
        PublisherSocket.Options.ReceiveHighWatermark = 1_000_000;   // Huge inbound queue
        PublisherSocket.Options.Backlog = 1024;                     // Enough for bursty connects
        PublisherSocket.Options.TcpKeepalive = true;
        PublisherSocket.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(30);
        PublisherSocket.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(10);
        PublisherSocket.Options.ReceiveBuffer = 1024 * 1024;        // OS recv buffer size
        PublisherSocket.Options.SendBuffer = 1024 * 1024;           // OS send buffer size
              
        // find random port in range of 10000 - 12000
        var port = PortFinder.FindAvailablePort(options.Value.RPCPort, port => PublisherSocket.Bind($"tcp://*:{port}"));
        endpoint.PubPort = port;
    }

    /// <summary>
    /// Adds a new DealerSocket for the given mesh node. 
    /// The Socket is only created if:
    /// 1. No Socket already exists for the node, AND
    /// 2. The node is on the local machine.
    /// </summary>
    /// <param name="info">The mesh node information used to configure the Socket.</param>
    public void AddSocket(MeshInfo info)
    {
        _scheduler.Invoke(poller =>
        {
            var subSocket = new SubscriberSocket();

            // Connect to the remote node’s PUB Socket
            subSocket.Connect($"tcp://{info.Address}:{info.PubPort}");

            // Subscribe to all topics from this publisher
            foreach (var topic in _eventHandlerProvider.GetRegisteredTopics())
            {
                subSocket.Subscribe(topic);
            }

            // Hook message receive _replyHandler
            subSocket.ReceiveReady += _eventReceivedHandler.OnEventReceived;

            // RegisterSelf the Socket with the poller
            poller.Add(subSocket);
        });
    }

    /// <summary>
    /// Removes and disposes the Socket for the specified mesh node. 
    /// Schedules cleanup work on the scheduler thread.
    /// </summary>
    /// <param name="meshInfo">Mesh node identifying which Socket to remove.</param>
    public void RemoveSocket(MeshInfo meshInfo)
    {
        _scheduler.Invoke(poller =>
        {
            for (int i = 0; i < _socketInfoList.Count; i++)
            {
                var socketInfo = _socketInfoList[i];

                if (socketInfo.Id == meshInfo.Id)
                {
                    // Finally, remove from dictionary.
                    _socketInfoList.RemoveAt(i);

                    // Remove Socket from poller first.
                    poller.Remove(socketInfo.Socket);

                    // Unsubscribe + dispose Socket.
                    CleanupSocket(socketInfo.Socket);
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Disposes this manager and all associated sockets.
    /// Ensures event unsubscription and thread-safe cleanup.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop receiving mesh updates.
        _eventAggregator.Unsubscribe(_onMeshJoined);
        _eventAggregator.Unsubscribe(_onMeshRemoved);

        // Clean up sockets on scheduler thread.
        _scheduler.Invoke(poller =>
        {
            foreach (var socketinfo in _socketInfoList)
            {
                poller.Remove(socketinfo.Socket);
                CleanupSocket(socketinfo.Socket);
            }
            _socketInfoList.Clear();
        });

        // Dispose the scheduler after all scheduled work is done.
        _scheduler.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Safely unsubscribes handlers and disposes a DealerSocket.
    /// Must always be called on the scheduler thread.
    /// </summary>
    private void CleanupSocket(SubscriberSocket socket)
    {
        socket.ReceiveReady -= _eventReceivedHandler.OnEventReceived!;
        socket.Dispose();
    }
}


