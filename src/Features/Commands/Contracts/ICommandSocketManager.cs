using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using Faster.Transport;
using Faster.Transport.Contracts;
using NetMQ.Sockets;

/// <summary>
/// Defines the contract for a command processor that manages socket lifecycles and command scheduling.
/// </summary>
public interface ICommandSocketManager : IDisposable
{
    
    /// <summary>
    /// Gets the number of sockets currently being managed.
    /// </summary>
    int Count { get; }
    TransportMode TransportMode { get; set; }

    /// <summary>
    /// Schedules the creation and addition of a new socket for a given mesh node.
    /// </summary>
    void AddSocket(MeshContext info);

    /// <summary>
    /// Sets the strategy used to validate whether a socket should be created.
    /// </summary>
    void AddSocketValidation(SocketValidationDelegate socketValidationDelegate);
    /// <summary>
    /// Returns an enumerable collection of managed sockets that are eligible for the given topic, up to the specified count.
    /// </summary>
    /// <param name="count">The maximum number of sockets to return. Enumeration stops once this count is reached.</param>
    /// <param name="topic">The topic hash used to filter sockets based on their command routing table.</param>
    /// <returns>
    /// An <see cref="IEnumerable{T}"/> of tuples:
    /// - <c>Id</c>: The unique mesh ID of the socket.
    /// - <c>Info</c>: The <see cref="MeshContext"/> containing routing and metadata.
    /// - <c>Socket</c>: The actual <see cref="DealerSocket"/> instance associated with the mesh ID.
    /// </returns>
    /// <remarks>
    /// This method filters sockets using the <see cref="CommandRoutingFilter"/> associated with each socket. 
    /// Only sockets whose routing table contains the specified <paramref name="topic"/> are returned.
    /// Enumeration stops as soon as <paramref name="count"/> sockets are yielded, even if more eligible sockets exist.
    /// </remarks>
    IEnumerable<IParticle> Get(int count, ulong topic);

    /// <summary>
    /// Schedules the removal of the socket for a specified mesh node.
    /// </summary>
    void RemoveSocket(MeshContext meshInfo);

}