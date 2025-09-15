using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using NetMQ.Sockets;
using System.Text;

namespace Faster.MessageBus.Features.Commands.Scope.Local;

/// <summary>
/// An internal implementation of <see cref="ILocalSocketManager"/> that creates and configures a <see cref="DealerSocket"/>
/// for local communication, ensuring its operations are safely executed on a dedicated command processing thread.
/// </summary>
internal class LocalSocketManager : ILocalSocketManager
{
    /// <summary>
    /// Gets the managed local <see cref="DealerSocket"/> instance.
    /// </summary>
    /// <value>
    /// The configured local <see cref="DealerSocket"/>.
    /// </value>
    /// <remarks>
    /// <strong style="color: red;">Warning:</strong> NetMQ sockets are not thread-safe. All operations on this socket
    /// are managed via the <see cref="ICommandScheduler"/> to ensure they occur on the correct thread.
    /// </remarks>
    public DealerSocket LocalSocket { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalSocketManager"/> class.
    /// </summary>
    /// <remarks>
    /// This constructor schedules the creation and configuration of the <see cref="DealerSocket"/>
    /// on the provided <paramref name="commandScheduler"/>'s thread. This is a crucial pattern that ensures
    //  the socket is "owned" by the network processing thread from its inception, guaranteeing all
    /// subsequent operations and event handling are thread-safe.
    /// </remarks>
    /// <param name="commandScheduler">The scheduler that provides the correct thread context for all socket operations.</param>
    /// <param name="commandReplyHandler">The handler that will process incoming reply messages on the socket.</param>
    /// <param name="localEndpoint">The configuration object that provides the connection details (address, port, identity).</param>
    public LocalSocketManager(ICommandScheduler commandScheduler,
        ICommandReplyHandler commandReplyHandler,
        LocalEndpoint localEndpoint)
    {
        // Use the commandScheduler to ensure all NetMQ operations happen on the poller thread.
        commandScheduler.Invoke(() =>
        {
            LocalSocket = new DealerSocket();

            // The Identity is crucial for the Router socket on the other end to identify this client.
            LocalSocket.Options.Identity = Encoding.UTF8.GetBytes(localEndpoint.Meshinfo.Id);

            // Wire up the handler for incoming messages. This event will fire on the scheduler's thread.
            LocalSocket.ReceiveReady += commandReplyHandler.ReceivedFromRouter!;

            // Connect the socket to its corresponding local endpoint.
            LocalSocket.Connect($"tcp://{localEndpoint.Meshinfo.Address}:{localEndpoint.Meshinfo.RpcPort}");
        });
    }
}