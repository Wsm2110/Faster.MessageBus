using Faster.MessageBus.Shared;
using NetMQ.Sockets;

namespace Faster.MessageBus.Features.Commands.Contracts;

/// <summary>
/// Defines a contract for managing a collection of NetMQ <see cref="DealerSocket"/> instances.
/// </summary>
/// <remarks>
/// This interface is typically implemented by a class responsible for maintaining and load-balancing
/// connections to multiple nodes in a service mesh or cluster. It handles the lifecycle of these
/// socket connections, allowing services to be added or removed dynamically.
/// </remarks>
public interface INetworkSocketManager : IDisposable
{
    // Note: While Dispose() is listed, it's best practice to formally inherit from IDisposable
    // to make the contract explicit to the compiler and other developers.

    /// <summary>
    /// Gets the current number of active sockets being managed.
    /// </summary>
    /// <value>
    /// The total count of managed <see cref="DealerSocket"/> instances.
    /// </value>
    int Count { get; }

    /// <summary>
    /// Gets a collection of all <see cref="DealerSocket"/> instances currently being managed.
    /// </summary>
    /// <returns>An enumerable collection of the active dealer sockets.</returns>
    /// <remarks>
    /// The returned collection may be modified by other threads. If a stable snapshot is needed,
    /// consider creating a copy (e.g., by calling <c>.ToList()</c>).
    /// </remarks>
    IEnumerable<DealerSocket> All { get; }

    /// <summary>
    /// Creates, configures, and adds a new <see cref="DealerSocket"/> to the manager.
    /// </summary>
    /// <param name="info">
    /// A <see cref="MeshInfo"/> object containing the necessary information, such as the endpoint address,
    /// to establish the new socket connection.
    /// </param>
    void AddSocket(MeshInfo info);

    /// <summary>
    /// Finds, closes, and removes a socket from the manager based on its connection information.
    /// </summary>
    /// <param name="meshInfo">The <see cref="MeshInfo"/> object used to identify the socket to be removed.</param>
    /// <returns>
    /// <c>true</c> if a matching socket was found and successfully removed; otherwise, <c>false</c>.
    /// </returns>
    bool RemoveSocket(MeshInfo meshInfo);
}