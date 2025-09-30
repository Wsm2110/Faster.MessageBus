using Faster.MessageBus.Shared;
using NetMQ.Sockets;

namespace Faster.MessageBus.Features.Events.Contracts;

/// <summary>
/// Defines a contract for a manager that handles the lifecycle of NetMQ sockets for event communication.
/// Implementations are responsible for creating, tracking, and disposing of sockets as nodes join and leave the mesh.
/// </summary>
public interface IEventSocketManager
{
    /// <summary>
    /// Gets or sets the single, outbound publisher socket used by this node to broadcast its own events.
    /// </summary>
    PublisherSocket PublisherSocket { get; set; }

    /// <summary>
    /// Creates and configures a new socket to connect to a remote node.
    /// </summary>
    /// <param name="info">The mesh node information containing the address and _port to connect to.</param>
    void AddSocket(MeshContext info);

    /// <summary>
    /// Finds, removes, and disposes the socket connected to a specified mesh node.
    /// </summary>
    /// <param name="meshInfo">The mesh node identifying which socket to remove.</param>
    void RemoveSocket(MeshContext meshInfo);

    /// <summary>
    /// Disposes the manager and all associated sockets and resources.
    /// </summary>
    void Dispose();
}