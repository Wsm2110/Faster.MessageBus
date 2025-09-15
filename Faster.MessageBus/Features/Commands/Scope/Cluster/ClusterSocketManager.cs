using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;
using NetMQ.Sockets;
using System.Text;

namespace Faster.MessageBus.Features.Commands.Scope.Cluster;

/// <summary>
/// Dynamically manages a collection of <see cref="DealerSocket"/> connections to other nodes in a cluster.
/// </summary>
/// <remarks>
/// This class ensures all socket operations are thread-safe by marshalling them onto a dedicated <see cref="ICommandScheduler"/> thread.
/// It automatically adds and removes connections by listening to <see cref="MeshJoined"/> and <see cref="MeshRemoved"/> events,
/// and can filter which nodes to connect to based on the provided <see cref="ClusterOptions"/>.
/// </remarks>
internal class ClusterSocketManager : IClusterSocketManager, IDisposable
{
    /// <summary>
    /// A dictionary of active client sockets, keyed by the mesh information of the node they are connected to.
    /// Access to this dictionary is synchronized via the <see cref="_scheduler"/>.
    /// </summary>
    private readonly Dictionary<MeshInfo, DealerSocket> _sockets = new();

    /// <summary>
    /// The scheduler that ensures all socket operations occur on a single, safe thread.
    /// </summary>
    private readonly ICommandScheduler _scheduler;

    /// <summary>
    /// The handler responsible for processing incoming replies from the sockets.
    /// </summary>
    private readonly ICommandReplyHandler _handler;

    /// <summary>
    /// The configuration options used to filter which cluster nodes to connect to.
    /// </summary>
    private readonly IOptions<ClusterOptions> _clusterOptions;

    /// <summary>
    /// Gets a collection of all currently managed <see cref="DealerSocket"/> instances.
    /// </summary>
    /// <remarks>
    /// The returned collection should be treated as a snapshot. Iterating over it while connections
    /// are being added or removed by another thread may lead to exceptions.
    /// </remarks>
    public IEnumerable<DealerSocket> All => _sockets.Values;

    /// <summary>
    /// Gets the current number of active socket connections to cluster nodes.
    /// </summary>
    /// <value>The number of managed sockets.</value>
    public int Count => _sockets.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterSocketManager"/> class.
    /// </summary>
    /// <param name="scheduler">The scheduler used to serialize access to sockets and internal collections.</param>
    /// <param name="handler">The handler for processing incoming message replies.</param>
    /// <param name="clusterOptions">The configuration for filtering which nodes to connect to.</param>
    public ClusterSocketManager(ICommandScheduler scheduler, ICommandReplyHandler handler, IOptions<ClusterOptions> clusterOptions)
    {
        // Subscribe to discovery events to dynamically manage connections as nodes join and leave the cluster.
        EventAggregator.Subscribe<MeshJoined>(data => AddSocket(data.Info));
        EventAggregator.Subscribe<MeshRemoved>(data => RemoveSocket(data.Info));

        _scheduler = scheduler;
        _handler = handler;
        _clusterOptions = clusterOptions;
    }

    /// <summary>
    /// Creates, configures, and connects a new <see cref="DealerSocket"/> based on the provided mesh information.
    /// The entire operation is performed on the scheduler's thread for thread safety.
    /// </summary>
    /// <remarks>
    /// A connection will only be established if the node's information passes the filter criteria
    /// defined in <see cref="ClusterOptions"/> and if a connection to that node does not already exist.
    /// </remarks>
    /// <param name="info">The connection and identity information for the new socket.</param>
    public void AddSocket(MeshInfo info)
    {
        // Use the scheduler to ensure all NetMQ operations and collection modifications happen on the poller thread.
        _scheduler.Invoke(() =>
        {
            // Note: This filtering logic may need review. As written, it rejects a node if *any* configured
            // application doesn't match, or if *any* configured node IP doesn't match.
            if (_clusterOptions.Value.Applications.Any() && _clusterOptions.Value.Applications.Exists(app => app.Name != info.Name))
            {
                return;
            }

            if (_clusterOptions.Value.Nodes?.Exists(node => node.IpAddress != info.Address) ?? false)
            {
                return;
            }

            // Don't add if a socket for this info already exists.
            if (_sockets.ContainsKey(info))
            {
                return;
            }

            var socket = new DealerSocket();
            // The Identity is crucial for the Router socket on the other end to identify the client.
            socket.Options.Identity = Encoding.UTF8.GetBytes(info.Id);
            socket.ReceiveReady += _handler.ReceivedFromRouter!;
            socket.Connect($"tcp://{info.Address}:{info.RpcPort}");

            // Add the newly created socket to our collection and notify the system.
            _sockets[info] = socket;
            EventAggregator.Publish(new DealerSocketCreated(socket));
        });
    }

    /// <summary>
    /// Finds, closes, disposes, and removes a socket connection to a specific cluster node.
    /// The entire operation is performed on the scheduler's thread for thread safety.
    /// </summary>
    /// <param name="meshInfo">The mesh information identifying the socket to remove.</param>
    /// <returns><c>true</c> if a socket was found and removed; otherwise, <c>false</c>.</returns>
    public bool RemoveSocket(MeshInfo meshInfo)
    {
        bool removed = false;

        // Ensure all operations on the dictionary and socket happen on the scheduler's thread.
        _scheduler.Invoke(() =>
        {
            if (_sockets.TryGetValue(meshInfo, out var socket))
            {
                if (_sockets.Remove(meshInfo))
                {
                    removed = true;
                    // Unsubscribe the event handler, dispose the socket, and notify the system.
                    socket.ReceiveReady -= _handler.ReceivedFromRouter!;
                    socket.Dispose();
                    EventAggregator.Publish(new DealerSocketRemoved(socket));
                }
            }
        });

        return removed;
    }

    /// <summary>
    /// Disposes all managed sockets and the scheduler itself.
    /// This operation is scheduled on the scheduler's thread for a graceful shutdown.
    /// </summary>
    public void Dispose()
    {
        // Schedule the disposal of all sockets on the scheduler's thread.
        _scheduler.Invoke(() =>
        {
            foreach (var s in _sockets.Values)
            {
                s.Dispose();
            }
            _sockets.Clear();
        });

        // After the above action is processed, the scheduler itself can be disposed.
        // Depending on the implementation, this might need to happen outside this invocation.
        _scheduler.Dispose();
    }
}