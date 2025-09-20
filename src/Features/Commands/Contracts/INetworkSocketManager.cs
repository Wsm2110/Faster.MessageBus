using Faster.MessageBus.Shared;
using NetMQ.Sockets;

namespace Faster.MessageBus.Features.Commands.Contracts;

/// <summary>
/// Defines a contract for managing a collection of NetMQ <see cref="DealerSocket"/> instances.
/// </summary>
/// <remarks>
/// This interface is typically implemented by a class responsible for maintaining and load-balancing
/// connections to multiple nodes in a service mesh or cluster. It handles the lifecycle of these
/// Socket connections, allowing services to be added or removed dynamically.
/// </remarks>
public interface INetworkSocketManager : IDisposable
{
    /// <summary>
    /// Returns up to <paramref name="count"/> sockets.
    /// If fewer sockets are available, returns all of them.
    /// If more are available, excess are skipped.
    /// </summary>
    public IEnumerable<(string Id, DealerSocket Socket)> Get(int count);

    /// <summary>
    /// Gets the total number of active sockets being managed.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Creates, configures, and adds a new <see cref="DealerSocket"/> for the given mesh node.
    /// The operation should be executed on the scheduler thread to ensure thread safety.
    /// </summary>
    /// <param name="info">The information of the mesh node to connect to.</param>
    void AddSocket(MeshInfo info);

    /// <summary>
    /// Removes and disposes the <see cref="DealerSocket"/> associated with the specified mesh node.
    /// Must be executed on the scheduler thread to maintain thread safety.
    /// </summary>
    /// <param name="meshInfo">The mesh node whose Socket should be removed.</param>
    void RemoveSocket(MeshInfo meshInfo);
}