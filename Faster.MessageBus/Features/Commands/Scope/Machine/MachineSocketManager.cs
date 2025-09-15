using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Scope.Network;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using NetMQ.Sockets;
using System.Text;

namespace Faster.MessageBus.Features.Commands.Scope.Machine;

internal class MachineSocketManager : IMachineSocketManager
{

    /// <summary>
    /// A dictionary of active client sockets, keyed by the mesh information of the node they are connected to.
    /// Access to this dictionary should be synchronized via the _scheduler.
    /// </summary>
    private readonly Dictionary<MeshInfo, DealerSocket> _sockets = new();
    private readonly ICommandScheduler _scheduler;
    private readonly ICommandReplyHandler _handler;

    /// <summary>
    /// Returns all managed sockets.
    /// </summary>
    public IEnumerable<DealerSocket> All => _sockets.Values;

    /// <summary>
    /// 
    /// </summary>
    public int Count => _sockets.Count;

    public MachineSocketManager(ICommandScheduler scheduler, ICommandReplyHandler commandReplyHandler)
    {
        EventAggregator.Subscribe<MeshJoined>(data => AddSocket(data.Info));
        EventAggregator.Subscribe<MeshRemoved>(data => RemoveSocket(data.Info));

        _scheduler = scheduler;
        _handler = commandReplyHandler;
    }

    /// <summary>
    /// Creates, configures, and connects a new DealerSocket based on the provided mesh information.
    /// The entire operation is performed on the _scheduler's thread for thread safety.
    /// </summary>
    /// <param name="info">The connection and identity information for the new socket.</param>
    public void AddSocket(MeshInfo info)
    {
        // Use the _scheduler to ensure all NetMQ operations happen on the poller thread.
        _scheduler.Invoke(() =>
        {
            // Don't add if a socket for this info already exists.ine
            // Only add sockets when the mesh is on the same mach
            if (_sockets.ContainsKey(info) || info.Address != LocalEndpoint.GetLocalIPv4())
            {
                return;
            }

            var socket = new DealerSocket();
            // The Identity is crucial for the Router socket on the other end to identify the client.
            socket.Options.Identity = Encoding.UTF8.GetBytes(info.Id);
            socket.ReceiveReady += _handler.ReceivedFromRouter!;
            socket.Connect($"tcp://{info.Address}:{info.RpcPort}");
            // Add the newly created socket to our collection.
            _sockets[info] = socket;
            EventAggregator.Publish(new DealerSocketCreated(socket));
        });
    }

    /// <summary>
    /// Removes and disposes of a socket.
    /// NOTE: This method is not fully implemented.
    /// </summary>
    /// <param name="key">The key identifying the socket to remove.</param>
    /// <returns>True if a socket was removed; otherwise, false.</returns>
    public bool RemoveSocket(MeshInfo meshInfo)
    {
        bool removed = false;

        // Ensure all operations on the dictionary happen on the _scheduler's thread.
        _scheduler.Invoke(() =>
        {
            if (_sockets.TryGetValue(meshInfo, out var socket))
            {
                // Remove the socket from the dictionary
                removed = _sockets.Remove(meshInfo);

                if (removed)
                {
                    // Unsubscribe the event handler and dispose the socket safely on the poller thread
                    socket.ReceiveReady -= _handler.ReceivedFromRouter!;
                    socket.Dispose();

                    // Publish an event so others can react to this socket being removed
                    // Note: notify commanddispatcher to stop polling
                    EventAggregator.Publish(new DealerSocketRemoved(socket));
                }
            }
        });

        return removed;
    }

    /// <summary>
    /// Disposes all managed sockets and the _scheduler itself.
    /// This operation is scheduled on the _scheduler's thread for a graceful shutdown.
    /// </summary>
    public void Dispose()
    {
        // Schedule the disposal of all sockets on the _scheduler's thread.
        _scheduler.Invoke(() =>
        {
            foreach (var s in _sockets.Values)
            {
                s.Dispose();
            }
            _sockets.Clear();
        });

        // After the above action is processed, dispose of the _scheduler itself.
        _scheduler.Dispose();
    }



}

