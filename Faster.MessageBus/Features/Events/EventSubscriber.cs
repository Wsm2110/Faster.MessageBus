using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Shared;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Concurrent;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// NetMQ-based subscriber that:
/// - Connects to PUB sockets of joined peers
/// - Subscribes to all topics
/// - Routes messages to topic handlers
/// - Uses a poller thread and action queue for thread-safe Socket operations
/// </summary>
internal class EventSubscriber : IEventSubscriber, IDisposable
{
    #region Fields

    private readonly IEventHandlerProvider _notificationHandlerProvider;
    private readonly NetMQPoller _poller;
    private readonly NetMQQueue<Action> _actionQueue;
    private readonly Thread _pollerThread;

    // Tracks active SubscriberSockets mapped by node Id
    private readonly ConcurrentDictionary<string, SubscriberSocket> _subSockets = new();

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSubscriber"/> class.
    /// Subscribes to mesh join/leave events and sets up the NetMQ poller thread.
    /// </summary>
    /// <param name="notificationHandlerProvider">Handler _messageHandler for incoming messages.</param>
    public EventSubscriber(IEventHandlerProvider notificationHandlerProvider)
    {
        _notificationHandlerProvider = notificationHandlerProvider;

        // Queue used to execute Socket operations safely on the poller thread
        _actionQueue = new NetMQQueue<Action>();
        _actionQueue.ReceiveReady += (_, e) =>
        {
            while (_actionQueue.TryDequeue(out var action, TimeSpan.FromMilliseconds(1)))
            {
                action?.Invoke();
            }
        };

        // Poller manages the sockets and action queue
        _poller = new NetMQPoller { _actionQueue };

        // Dedicated background thread running the poller loop
        _pollerThread = new Thread(_poller.Run)
        {
            IsBackground = true
        };

        // When a new node joins, connect to its PUB Socket
        EventAggregator.Subscribe<MeshJoined>(joined =>
        {
            _actionQueue.Enqueue(() =>
            {
                if (_subSockets.ContainsKey(joined.Info.Id))
                {
                    return;
                }

                try
                {

                    var subSocket = new SubscriberSocket();

                    // Connect to the remote node’s PUB Socket
                    subSocket.Connect($"tcp://{joined.Info.Address}:{joined.Info.PubPort}");

                    // Subscribe to all topics from this publisher

                    foreach (var topic in notificationHandlerProvider.GetRegisteredTopics())
                    {
                        subSocket.Subscribe(topic);
                    }

                    // Hook message receive _handler
                    subSocket.ReceiveReady += OnMessageReceived;

                    // RegisterSelf the Socket with the poller
                    _poller.Add(subSocket);

                    _subSockets.TryAdd(joined.Info.Id, subSocket);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            });
        });

        // When a node leaves, disconnect and dispose its Socket
        EventAggregator.Subscribe<MeshRemoved>(left =>
        {
            _actionQueue.Enqueue(() =>
            {
                if (_subSockets.TryRemove(left.Info.Id, out var socket))
                {
                    socket.ReceiveReady -= OnMessageReceived;
                    _poller.Remove(socket);
                    socket.Dispose();
                }
            });
        });

        Start();
    }

    /// <summary>
    /// Starts the subscriber's poller thread.
    /// </summary>
    public void Start() => _pollerThread.Start();

    /// <summary>
    /// Stops the poller and cleans up all sockets and resources.
    /// </summary>
    public void Stop()
    {
        _poller.Stop();
        _poller.Dispose();
        _actionQueue.Dispose();
        _pollerThread.Join();
    }

    /// <summary>
    /// Disposes the subscriber and stops the poller.
    /// </summary>
    public void Dispose() => Stop();

    /// <summary>
    /// Handles messages received from any subscribed Socket.
    /// Deserializes and dispatches to the registered _handler.
    /// </summary>
    private void OnMessageReceived(object? sender, NetMQSocketEventArgs e)
    {
        NetMQMessage msg = new NetMQMessage();
        while (e.Socket.TryReceiveMultipartMessage(ref msg, 2))
        {
            // Route the message to the appropriate _handler
            _notificationHandlerProvider.GetHandler(msg[0].ConvertToString()).Invoke(msg[1].Buffer);
        }
    }
}
