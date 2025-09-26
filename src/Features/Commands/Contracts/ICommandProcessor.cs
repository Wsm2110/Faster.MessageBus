using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using NetMQ.Sockets;

/// <summary>
/// Defines the contract for a command processor that manages socket lifecycles and command scheduling.
/// </summary>
public interface ICommandProcessor : IDisposable
{
    /// <summary>
    /// Gets the number of sockets currently being managed.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Schedules the creation and addition of a new socket for a given mesh node.
    /// </summary>
    void AddSocket(MeshInfo info);

    /// <summary>
    /// Sets the strategy used to validate whether a socket should be created.
    /// </summary>
    void AddSocketStrategy(ISocketStrategy addMachineSocketStrategy);

    /// <summary>
    /// Returns an enumerable collection of managed sockets.
    /// </summary>
    IEnumerable<(ulong Id, (MeshInfo Info, DealerSocket Socket))> Get(int count);

    /// <summary>
    /// Schedules the removal of the socket for a specified mesh node.
    /// </summary>
    void RemoveSocket(MeshInfo meshInfo);

    /// <summary>
    /// Schedules a command to be sent over a socket.
    /// </summary>
    void ScheduleCommand(ScheduleCommand command);
}