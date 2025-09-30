using Faster.MessageBus.Shared;
using NetMQ;

namespace Faster.MessageBus.Features.Discovery.Contracts;

/// <summary>
/// Represents a discovery service that listens for and broadcasts node information (MeshContext)
/// over the network, typically using a mechanism like the NetMQ beacon for peer discovery.
/// </summary>
public interface IMeshDiscoveryService : IDisposable
{
    /// <summary>
    /// Starts the discovery service. This begins broadcasting the local node's MeshContext
    /// and listening for beacons from other nodes on the network.
    /// </summary>
    void Start(MeshContext info);

    /// <summary>
    /// Stops the discovery service. This ceases broadcasting and listening for beacons.
    /// </summary>
    void Stop();

    /// <summary>
    /// TryRemove nodes that were previously discovered but are now considered inactive.
    /// </summary>
    /// <remarks>
    /// A node is typically marked as inactive if its discovery beacon has not been received
    /// for a predefined timeout period. This method is essential for detecting and cleaning up
    /// dead or unreachable nodes from the cluster's membership list.
    /// </remarks>
    /// <returns>
    /// An enumerable collection of <see cref="MeshContext"/> objects for each node that is no longer active.
    /// </returns>
    void RemoveInactiveApplications(object? sender, NetMQTimerEventArgs args);
}