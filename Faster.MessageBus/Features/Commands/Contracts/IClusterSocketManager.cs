using Faster.MessageBus.Shared;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;

namespace Faster.MessageBus.Features.Commands.Contracts
{
    /// <summary>
    /// Defines a contract for a component that dynamically manages a collection of <see cref="DealerSocket"/> connections to various nodes within a cluster.
    /// </summary>
    /// <remarks>
    /// An implementation of this interface is responsible for the lifecycle of these sockets—creating them when nodes join the cluster,
    /// and closing them when nodes leave. This provides a centralized way to manage client-side connections in a distributed system.
    /// </remarks>
    public interface IClusterSocketManager : IDisposable
    {
        // Note: While Dispose() is listed, it's best practice to formally inherit from IDisposable
        // to make the contract explicit to the compiler and other developers.

        /// <summary>
        /// Gets a collection of all currently active <see cref="DealerSocket"/> instances managed by this manager.
        /// </summary>
        /// <returns>An enumerable collection of the active dealer sockets.</returns>
        /// <remarks>
        /// The returned collection may be modified by other threads as nodes join or leave the cluster.
        /// If a stable snapshot is needed for iteration, consider creating a copy (e.g., by calling <c>.ToList()</c>).
        /// </remarks>
        IEnumerable<DealerSocket> All { get; }

        /// <summary>
        /// Gets the current number of active socket connections to cluster nodes.
        /// </summary>
        /// <value>The number of managed sockets.</value>
        int Count { get; }

        /// <summary>
        /// Adds a new <see cref="DealerSocket"/> connection to a cluster node based on the provided information.
        /// </summary>
        /// <param name="info">A <see cref="MeshInfo"/> object containing the connection details and identity of the node to connect to.</param>
        void AddSocket(MeshInfo info);

        /// <summary>
        /// Finds, closes, and removes a socket connection to a specific cluster node.
        /// </summary>
        /// <param name="meshInfo">The <see cref="MeshInfo"/> object identifying the node connection to remove.</param>
        /// <returns><c>true</c> if a matching socket was found and successfully removed; otherwise, <c>false</c>.</returns>
        bool RemoveSocket(MeshInfo meshInfo);
    }
}