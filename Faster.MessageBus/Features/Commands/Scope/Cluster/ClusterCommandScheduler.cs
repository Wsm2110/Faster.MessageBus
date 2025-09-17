using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Extensions;
using NetMQ;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Implements the <see cref="ICommandScheduler"/> interface to provide a single-threaded execution context for processing commands and interacting with NetMQ sockets.
/// This class uses the "actor" model, where work is enqueued from any thread and executed sequentially on a dedicated worker thread, ensuring thread safety without explicit locks.
/// </summary>
public sealed class ClusterCommandScheduler : ICommandScheduler, IDisposable
{
    /// <summary>
    /// The dedicated thread where the NetMQPoller runs, providing a serialized execution context.
    /// </summary>
    private readonly Thread _thread;

    /// <summary>
    /// The poller that monitors sockets and other pollable objects for events. It forms the core of the event loop on the worker thread.
    /// </summary>
    private readonly NetMQPoller _poller = new NetMQPoller();

    /// <summary>
    /// A thread-safe queue for generic <see cref="Action"/> delegates. Actions are enqueued from any thread
    /// and executed sequentially on the poller's thread.
    /// </summary>
    private readonly NetMQQueue<Action<NetMQPoller>> _actionQueue = new NetMQQueue<Action<NetMQPoller>>();

    /// <summary>
    /// A thread-safe queue specifically for <see cref="ScheduleCommand"/> objects that need to be serialized and sent over a socket.
    /// This separation allows for specialized, high-performance handling of socket-bound commands.
    /// </summary>
    private readonly NetMQQueue<ScheduleCommand> _commandQueue = new NetMQQueue<ScheduleCommand>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterCommandScheduler"/> class.
    /// It sets up the queues, poller, subscribes to socket lifecycle events, and starts the dedicated worker thread.
    /// </summary>
    /// <param name="name">The name to assign to the worker thread for easier debugging.</param>
    public ClusterCommandScheduler(string name = "ClusterCommandSchedulerThread")
    {
        // Subscribe to the ReceiveReady events. These are triggered by the poller
        // on its own thread when an item is available in a queue.
        _actionQueue.ReceiveReady += OnQueueReceiveReady;
        _commandQueue.ReceiveReady += OnSocketQueueReceiveReady;

        // Add the queues to the poller. The poller will now monitor them for new items.
        _poller.Add(_actionQueue);
        _poller.Add(_commandQueue);

        // Create and configure the dedicated thread to run the poller's event loop.
        _thread = new Thread(() =>
        {
            _poller.Run();
        })
        { IsBackground = true, Name = name }; // Run as a background thread so it doesn't prevent application exit.

        // Start the thread, which begins executing the _poller.Run() loop.
        _thread.Start();
    }

    /// <summary>
    /// Event handler that processes socket-bound commands from the <see cref="_commandQueue"/> queue.
    /// This method is executed on the poller's dedicated thread.
    /// </summary>
    /// <remarks>
    /// The method constructs and sends a multi-part NetMQ message with the following structure:
    /// Frame 1: Empty (for ROUTER socket routing).
    /// Frame 2: Topic (ulong).
    /// Frame 3: CorrelationId (ulong).
    /// Frame 4: Payload (byte span).
    /// </remarks>
    private void OnSocketQueueReceiveReady(object? sender, NetMQQueueEventArgs<ScheduleCommand> e)
    {
        // Dequeue and process all currently available commands.
        while (_commandQueue.TryDequeue(out var command, TimeSpan.Zero))
        {
            Span<byte> topicBuffer = stackalloc byte[sizeof(ulong)];
            Span<byte> corrBuffer = stackalloc byte[sizeof(ulong)];

            // Write the ulong values directly into the stack-allocated spans for high performance.
            BitConverter.TryWriteBytes(topicBuffer, command.Topic);
            BitConverter.TryWriteBytes(corrBuffer, command.CorrelationId);

            // SendAsync the multi-part message.
            command.Socket.SendMoreFrameEmpty()
                          .SendSpanFrame(topicBuffer, true)
                          .SendSpanFrame(corrBuffer, true)
                          .SendSpanFrame(command.Payload.Span);
        }
    }

    /// <summary>
    /// Event handler that processes generic actions from the <see cref="_actionQueue"/>.
    /// This method is executed on the poller's dedicated thread.
    /// </summary>
    private void OnQueueReceiveReady(object? sender, NetMQQueueEventArgs<Action<NetMQPoller>> e)
    {
        // Dequeue and invoke all currently available actions.
        while (_actionQueue.TryDequeue(out var action, TimeSpan.Zero))
        {
            action(_poller);
        }
    }

    /// <summary>
    /// Schedules a generic action to be executed on the poller thread. This ensures that the action
    /// can safely interact with any resource managed by this processor, such as sockets.
    /// </summary>
    /// <param name="action">The action to execute on the poller thread.</param>
    public void Invoke(Action<NetMQPoller> action) => _actionQueue.Enqueue(action);

    /// <summary>
    /// Schedules a command to be serialized and sent over a socket via the poller thread.
    /// </summary>
    /// <param name="command">The command containing the socket, topic, correlation ID, and payload to send.</param>
    public void Invoke(ScheduleCommand command) => _commandQueue.Enqueue(command);

    /// <summary>
    /// Stops the poller, joins the worker thread, and cleans up all managed resources.
    /// </summary>
    public void Dispose()
    {
        // Enqueue the Stop command to the poller's own queue. This is the correct way
        // to gracefully shut down the poller from an external thread.
        _poller.Stop();
        // Wait for the poller's thread to finish its work and exit.
        _thread.Join();

        // Now that the thread is stopped, it is safe to dispose of the poller and queues.
        _poller.Dispose();
        _actionQueue.Dispose();
        _commandQueue.Dispose();
    }
}