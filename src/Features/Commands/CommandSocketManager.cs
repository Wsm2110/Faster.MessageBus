using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Scope.Machine;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;
using NetMQ.Sockets;
using System.Net;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Manages a collection of <see cref="DealerSocket"/> instances for communication 
/// with other nodes on the same machine. 
/// 
/// All Socket operations are executed on a dedicated scheduler thread 
/// to guarantee thread safety without locks.
/// </summary>
internal class CommandSocketManager : ICommandSocketManager, IDisposable
{
    /// <summary>
    /// Internal list of sockets keyed by MeshInfo.MeshId.
    /// </summary>
    private readonly List<(ulong Id, DealerSocket Socket)> _socketInfoList = new();

    /// <summary>
    /// Scheduler that provides single-threaded execution context for all Socket operations.
    /// </summary>
    private readonly ICommandScheduler _scheduler;

    /// <summary>
    /// Event aggregator for subscribing to mesh membership changes.
    /// </summary>
    private readonly IEventAggregator _eventAggregator;

    /// <summary>
    /// Handles replies received from RouterSockets.
    /// </summary>
    private readonly ICommandReplyHandler _handler;
    private readonly IOptions<MessageBrokerOptions> _options;

    /// <summary>
    /// Delegates stored for unsubscribing from mesh join/remove events during disposal.
    /// </summary>
    private readonly Action<MeshJoined> _onMeshJoined;
    private readonly Action<MeshRemoved> _onMeshRemoved;

    private bool _disposed;
    private ISocketStrategy _socketStrategy;

    /// <summary>
    /// Gets the number of sockets currently managed.
    /// </summary>
    public int Count => _socketInfoList.Count;

    /// <summary>
    /// Initializes a new instance of <see cref="MachineSocketManager"/>.
    /// Subscribes to mesh events and sets up the scheduler context.
    /// </summary>
    public CommandSocketManager(
        ICommandScheduler scheduler,
        IEventAggregator eventAggregator,
        ICommandReplyHandler commandReplyHandler,
        IOptions<MessageBrokerOptions> options, 
        LocalEndpoint localEndpoint)
    {
        _scheduler = scheduler;
        _eventAggregator = eventAggregator;
        _handler = commandReplyHandler;
        _options = options;

        // Store delegates for later unsubscription.
        _onMeshJoined = data => AddSocket(data.Info);
        _onMeshRemoved = data => RemoveSocket(data.Info);

        // Subscribe to cluster mesh lifecycle events.
        eventAggregator.Subscribe(_onMeshJoined);
        eventAggregator.Subscribe(_onMeshRemoved);   
    }

    /// <summary>
    /// Returns up to <paramref name="count"/> sockets.
    /// If fewer sockets are available, returns all of them.
    /// If more are available, excess are skipped.
    /// </summary>
    public IEnumerable<(ulong Id, DealerSocket Socket)> Get(int count)
    {
        int take = Math.Min(count, _socketInfoList.Count);   // cap at requested count

        for (int i = 0; i < take; i++)
        {
            yield return _socketInfoList[i];
        }
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
            if (_socketStrategy != null && !_socketStrategy.Validate(info, _options))
            {
                return;
            }

            var socket = new DealerSocket
            {
                // Assign a unique compact identity.
                Options = { Identity = DealerIdentityGenerator.Create() }
            };

            // Register reply handler for incoming messages.
            socket.ReceiveReady += _handler.ReceivedFromRouter!;

            // Connect to the remote node's RPC endpoint.
            socket.Connect($"tcp://{info.Address}:{info.RpcPort}");

            // Track Socket and add it to the poller.
            _socketInfoList.Add((info.MeshId, socket));
            poller.Add(socket);
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

                if (socketInfo.Id == meshInfo.MeshId)
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
    private void CleanupSocket(DealerSocket socket)
    {
        socket.ReceiveReady -= _handler.ReceivedFromRouter!;
        socket.Dispose();
    }

    public void AddSocketStrategy(ISocketStrategy addMachineSocketStrategy)
    {
        _socketStrategy = addMachineSocketStrategy;
    }
}
