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
}