using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Faster.MessageBus.Features.Commands.Scope.Network;

/// <summary>
/// Manages a collection of client (Dealer) sockets that connect to other nodes in the mesh.
/// It uses a <see cref="ICommandScheduler"/> to ensure that all socket operations
/// (creation, configuration, disposal) are performed on a single, dedicated thread,
/// guaranteeing NetMQ's required thread affinity.
/// </summary>
public sealed class NetworkSocketManager : IDisposable, INetworkSocketManager
{
    /// <summary>
    /// A dictionary of active client sockets, keyed by the mesh information of the node they are connected to.
    /// Access is synchronized via the _scheduler.
    /// </summary>
    private readonly Dictionary<MeshInfo, DealerSocket> _sockets = new();
    private readonly ICommandScheduler _scheduler;
    private readonly IEventAggregator _eventAggregator;
    private readonly ICommandReplyHandler _handler;

    // Actions to hold references to our event subscriptions for proper unsubscribing to prevent memory leaks.
    private readonly Action<MeshJoined> _onMeshJoined;
    private readonly Action<MeshRemoved> _onMeshRemoved;

    private bool _disposed = false;

    /// <summary>
    /// Returns all managed sockets.
    /// </summary>
    public IEnumerable<DealerSocket> All => _sockets.Values;

    /// <summary>
    /// Gets the number of active sockets managed by this instance.
    /// </summary>
    public int Count => _sockets.Count;

    public NetworkSocketManager([FromKeyedServices("networkCommandScheduler")] ICommandScheduler scheduler,
        IEventAggregator eventAggregator,
        ICommandReplyHandler handler)
    {
        _scheduler = scheduler;
        _eventAggregator = eventAggregator;
        _handler = handler;

        // Store the delegates so we can unsubscribe from them during disposal.
        _onMeshJoined = data => AddSocket(data.Info);
        _onMeshRemoved = data => RemoveSocket(data.Info);

        _eventAggregator.Subscribe(_onMeshJoined);
        _eventAggregator.Subscribe(_onMeshRemoved);
    }

    /// <summary>
    /// Creates, configures, and connects a new DealerSocket based on the provided mesh information.
    /// The entire operation is performed on the scheduler's thread for thread safety.
    /// </summary>
    /// <param name="info">The connection and identity information for the new socket.</param>
    public void AddSocket(MeshInfo info)
    {
        _scheduler.Invoke(poller =>
        {
            // Don't add if a socket for this info already exists.
            if (_sockets.ContainsKey(info))
            {
                return;
            }

            var socket = new DealerSocket();
            socket.Options.Identity = Encoding.UTF8.GetBytes($"network-{info.Id}");
            socket.ReceiveReady += _handler.ReceivedFromRouter!;
            socket.Connect($"tcp://{info.Address}:{info.RpcPort}");

            _sockets[info] = socket;
            poller.Add(socket);
        });
    }

    /// <summary>
    /// Removes and disposes of a socket associated with the given mesh information.
    /// The entire operation is performed on the scheduler's thread for thread safety.
    /// </summary>
    /// <param name="meshInfo">The key identifying the socket to remove.</param>
    /// <returns>True if a socket was found and removed; otherwise, false.</returns>
    public bool RemoveSocket(MeshInfo meshInfo)
    {
        bool removed = false;
        _scheduler.Invoke(poller =>
        {
            if (_sockets.TryGetValue(meshInfo, out var socket))
            {
                // Unsubscribe, dispose, and then remove from the collection.
                CleanupSocket(socket);
                removed = _sockets.Remove(meshInfo);
                poller.Remove(socket);
            }
        });
        return removed;
    }

    /// <summary>
    /// Disposes all managed resources, including event subscriptions, all sockets, and the scheduler.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // 1. Unsubscribe from global events to prevent memory leaks and stop receiving new events.
        _eventAggregator.Unsubscribe(_onMeshJoined);
        _eventAggregator.Unsubscribe(_onMeshRemoved);

        // 2. Schedule the cleanup of all network resources on the correct thread.
        // This is a blocking call, ensuring cleanup is finished before proceeding.
        _scheduler.Invoke(poller =>
        {
            foreach (var socket in _sockets.Values)
            {
                CleanupSocket(socket);
            }
            _sockets.Clear();
        });

        // 3. After all scheduled work is complete, dispose of the scheduler itself.
        _scheduler.Dispose();

        _disposed = true;
    }

    /// <summary>
    /// Helper method to safely unsubscribe and dispose of a socket.
    /// MUST be called from the scheduler's thread.
    /// </summary>
    private void CleanupSocket(DealerSocket socket)
    {
        if (socket == null) return;
        socket.ReceiveReady -= _handler.ReceivedFromRouter!;
        socket.Dispose();
    }
}