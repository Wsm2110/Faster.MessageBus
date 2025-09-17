using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using NetMQ;
using NetMQ.Sockets;
using System.Text;

namespace Faster.MessageBus.Features.Commands.Scope.Local;

/// <summary>
/// An internal implementation of <see cref="ILocalSocketManager"/> that creates and configures a <see cref="DealerSocket"/>
/// for local communication, ensuring its operations are safely executed on a dedicated command processing thread.
/// </summary>
internal class LocalSocketManager : ILocalSocketManager, IDisposable
{
    private readonly ICommandScheduler _commandScheduler;
    private readonly ICommandReplyHandler _commandReplyHandler;
    private readonly LocalEndpoint _localEndpoint;
    private bool _disposed = false;

    /// <summary>
    /// Gets the managed local <see cref="DealerSocket"/> instance.
    /// </summary>
    /// <value>
    /// The configured local <see cref="DealerSocket"/>. Will be null after the manager is disposed.
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
    /// the socket is "owned" by the network processing thread from its inception, guaranteeing all
    /// subsequent operations and event handling are thread-safe.
    /// </remarks>
    /// <param name="commandScheduler">The scheduler that provides the correct thread context for all socket operations.</param>
    /// <param name="commandReplyHandler">The handler that will process incoming reply messages on the socket.</param>
    /// <param name="localEndpoint">The configuration object that provides the connection details (address, port, identity).</param>
    public LocalSocketManager([FromKeyedServices("localCommandScheduler")] ICommandScheduler scheduler,
                              ICommandReplyHandler commandReplyHandler,
                              LocalEndpoint localEndpoint)
    {
        // Store dependencies needed for disposal
        _commandScheduler = scheduler;
        _commandReplyHandler = commandReplyHandler;
        _localEndpoint = localEndpoint;
    }

    public void Initialize()     
    {    
        // Use the commandScheduler to ensure all NetMQ operations happen on the poller thread.
        _commandScheduler.Invoke(poller =>
        {
            LocalSocket = new DealerSocket();

            // The Identity is crucial for the Router socket on the other end to identify this client.
            LocalSocket.Options.Identity = Encoding.UTF8.GetBytes($"Local-{_localEndpoint.MeshId}");

            // Wire up the handler for incoming messages. This event will fire on the scheduler's thread.
            LocalSocket.ReceiveReady += _commandReplyHandler.ReceivedFromRouter!;

            // Connect the socket to its corresponding local endpoint.
            LocalSocket.Connect($"tcp://{_localEndpoint.Address}:{_localEndpoint.RpcPort}");

            poller.Add(LocalSocket);
        });
    }

    /// <summary>
    /// Disposes the underlying <see cref="DealerSocket"/> in a thread-safe manner.
    /// </summary>
    /// <remarks>
    /// This method schedules the cleanup operations on the command processing thread to prevent race conditions.
    /// It ensures that the event handler is unsubscribed and the socket is properly closed and disposed.
    /// </remarks>
    public void Dispose()
    {
        Dispose(true);
        // A finalizer is not used here because cleanup MUST happen on the scheduler thread,
        // which cannot be guaranteed from the finalizer thread.
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Schedule the disposal of the socket on the correct thread.
            _commandScheduler.Invoke(poller =>
            {
                if (LocalSocket != null)
                {
                    // 1. Unsubscribe from the event to prevent memory leaks and dangling references.
                    LocalSocket.ReceiveReady -= _commandReplyHandler.ReceivedFromRouter!;

                    // 2. Dispose of the socket object, which also closes the connection.
                    LocalSocket.Dispose();

                    // 3. Set the property to null to prevent use-after-dispose.
                    LocalSocket = null;
                }
            });
        }

        _disposed = true;
    }
}