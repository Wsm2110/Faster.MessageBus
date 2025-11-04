using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using Faster.Transport;
using Faster.Transport.Contracts;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// A high-performance, thread-safe manager that maintains active <see cref="IParticle"/> connections
/// to other nodes in the mesh network.  
/// 
/// It subscribes to mesh lifecycle events (`MeshJoined`, `MeshRemoved`) and automatically adds or removes
/// sockets based on the network topology.  
/// 
/// The manager uses <see cref="ConcurrentDictionary{TKey, TValue}"/> for O(1) lookups and minimizes GC pressure
/// by avoiding intermediate data structures.
/// </summary>
/// <example>
/// Example usage:
/// <code>
/// var socketManager = new CommandSocketManager(eventAggregator, responseHandler, options);
/// socketManager.TransportMode = TransportMode.Tcp;
/// 
/// // Retrieve a socket that can handle a specific topic
/// var socket = socketManager.Get("pc2", topicHash);
/// if (socket != null)
///     socket.Send(payload);
/// </code>
/// </example>
public sealed class CommandSocketManager : ICommandSocketManager, IDisposable
{
    #region Fields

    private readonly ConcurrentDictionary<MeshContext, IParticle> _sockets = new();
    private readonly IEventAggregator _eventAggregator;
    private readonly ICommandResponseHandler _handler;
    private readonly IOptions<MessageBrokerOptions> _options;
    private readonly Action<MeshJoined> _onMeshJoined;
    private readonly Action<MeshRemoved> _onMeshRemoved;
    private SocketValidationDelegate? _socketStrategy;

    private volatile bool _disposed;
    private int _count;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the transport mode used when creating new sockets (TCP, IPC, or Inproc).
    /// </summary>
    public TransportMode TransportMode { get; set; }

    /// <summary>
    /// Gets the total number of active sockets managed by this instance.
    /// </summary>
    public int Count => _count;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandSocketManager"/> class and subscribes
    /// to mesh topology events for automatic socket management.
    /// </summary>
    public CommandSocketManager(
        IEventAggregator eventAggregator,
        ICommandResponseHandler handler,
        IOptions<MessageBrokerOptions> options)
    {
        _eventAggregator = eventAggregator;
        _handler = handler;
        _options = options;

        _onMeshJoined = data => AddSocket(data.Info);
        _onMeshRemoved = data => RemoveSocket(data.Info);

        _eventAggregator.Subscribe(_onMeshJoined);
        _eventAggregator.Subscribe(_onMeshRemoved);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Retrieves a collection of sockets that are eligible to handle the given topic.
    /// </summary>
    /// <param name="count">The maximum number of sockets to return.</param>
    /// <param name="topic">The topic hash to filter by.</param>
    /// <returns>An enumerable of <see cref="IParticle"/> instances.</returns>
    public IEnumerable<IParticle> Get(int count, ulong topic)
    {
        int yielded = 0;

        foreach (var pair in _sockets)
        {
            if (yielded >= count)
                break;

            var context = pair.Key;

            if (!CommandRoutingFilter.TryContains(context.CommandRoutingTable, topic))
                continue;

            yield return pair.Value;
            yielded++;
        }
    }

    /// <summary>
    /// Retrieves a single socket for a specific application and topic, or returns null if none match.
    /// </summary>
    public IParticle? Get(string applicationId, ulong topic)
    {
        foreach (var kvp in _sockets)
        {
            var context = kvp.Key;
            if (context.ApplicationName != applicationId)
                continue;

            if (CommandRoutingFilter.TryContains(context.CommandRoutingTable, topic))
                return kvp.Value;
        }

        return null;
    }

    /// <summary>
    /// Adds a new socket for the given mesh context. If the context already exists, the old socket is replaced.
    /// </summary>
    public void AddSocket(MeshContext context)
    {
        if (_disposed)
            return;

        // Apply custom socket validation if registered
        if (_socketStrategy != null && !_socketStrategy.Invoke(context, _options))
            return;

        IParticle particle;

        // Choose transport type
        switch (TransportMode)
        {
            case TransportMode.Inproc:
                particle = new ParticleBuilder()
                    .UseMode(TransportMode.Inproc)
                    .WithChannel(context.ApplicationName)
                    .OnReceived(_handler.ReceivedFromRouter!)
                    .Build();
                break;

            case TransportMode.Ipc:
                particle = new ParticleBuilder()
                    .UseMode(TransportMode.Ipc)
                    .WithChannel(context.ApplicationName)
                    .OnReceived(_handler.ReceivedFromRouter!)
                    .Build();
                break;

            case TransportMode.Tcp:
            default:
                particle = new ParticleBuilder()
                    .UseMode(TransportMode.Tcp)
                    .WithRemote(new IPEndPoint(IPAddress.Parse(context.Address), context.RpcPort))
                    .OnReceived(_handler.ReceivedFromRouter!)
                    .Build();
                break;
        }

        _sockets.AddOrUpdate(context, particle, (_, old) =>
        {
            old.Dispose();
            return particle;
        });

        Interlocked.Increment(ref _count);
    }

    /// <summary>
    /// Removes and disposes of the socket associated with the specified mesh node.
    /// </summary>
    public void RemoveSocket(MeshContext context)
    {
        if (_sockets.TryRemove(context, out var particle))
        {
            CleanupSocket(particle);
            Interlocked.Decrement(ref _count);
        }
    }

    /// <summary>
    /// Defines a validation delegate that determines whether a socket should be created for a given node.
    /// </summary>
    public void AddSocketValidation(SocketValidationDelegate socketValidationDelegate)
    {
        _socketStrategy = socketValidationDelegate;
    }

    #endregion

    #region Internal Helpers

    /// <summary>
    /// Disposes of a socket safely and detaches event handlers.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CleanupSocket(IParticle socket)
    {
        try
        {
            socket.OnReceived -= _handler.ReceivedFromRouter!;
            socket.Dispose();
        }
        catch
        {
            // Ignore shutdown races
        }
    }

    #endregion

    #region Disposal

    /// <summary>
    /// Gracefully disposes of all managed sockets and unsubscribes from mesh events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _eventAggregator.Unsubscribe(_onMeshJoined);
        _eventAggregator.Unsubscribe(_onMeshRemoved);

        foreach (var particle in _sockets.Values)
            CleanupSocket(particle);

        _sockets.Clear();

        GC.SuppressFinalize(this);
    }

    #endregion
}
