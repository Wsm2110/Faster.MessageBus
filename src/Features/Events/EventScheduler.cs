using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Features.Events.Shared;
using NetMQ;
using System.Runtime.InteropServices;
using System.Text;

namespace Faster.MessageBus.Features.Events;

// <summary>
/// Implements the <see cref="IEventScheduler"/> interface to provide a single-threaded execution context for processing commands and interacting with NetMQ sockets.
/// This class uses the "actor" model, where work is enqueued from any thread and executed sequentially on a dedicated worker thread, ensuring thread safety without explicit locks.
/// </summary>

public class EventScheduler : IEventScheduler, IDisposable
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
    /// A thread-safe queue specifically for <see cref="ScheduleCommand"/> objects that need to be serialized and sent over a Socket.
    /// This separation allows for specialized, high-performance handling of Socket-bound commands.
    /// </summary>
    private readonly NetMQQueue<ScheduleEvent> _eventQueue = new NetMQQueue<ScheduleEvent>();
    private bool disposedValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterCommandScheduler"/> class.
    /// It sets up the queues, poller, subscribes to Socket lifecycle events, and starts the dedicated worker thread.
    /// </summary>
    /// <param name="name">The name to assign to the worker thread for easier debugging.</param>
    public EventScheduler(string name = "EventSchedulerThread")
    {
        // Subscribe to the ReceiveReady events. These are triggered by the poller
        // on its own thread when an item is available in a queue.
        _actionQueue.ReceiveReady += OnQueueReceiveReady;
        _eventQueue.ReceiveReady += OnReceived;

        // TryAdd the queues to the poller. The poller will now monitor them for new items.
        _poller.Add(_actionQueue);
        _poller.Add(_eventQueue);

        // Create and configure the dedicated thread to run the poller's event loop.
        _thread = new Thread(() =>
        {
            _poller.RunAsync();
        })
        { IsBackground = true, Name = name }; // Run as a background thread so it doesn't prevent application exit.

        // Start the thread, which begins executing the _poller.Run() loop.
        _thread.Start();
    }

    /// <summary>
    /// Event handler that processes Socket-bound commands from the <see cref="_eventQueue"/> queue.
    /// This method is executed on the poller's dedicated thread.
    /// </summary>
    /// <remarks>
    /// The method constructs and sends a multi-part NetMQ message with the following structure:
    /// Frame 1: Empty (for ROUTER Socket routing).
    /// Frame 2: Topic (ulong).      
    /// Frame 4: Payload (byte span).
    /// </remarks>
    private void OnReceived(object? sender, NetMQQueueEventArgs<ScheduleEvent> e)
    {
        // Dequeue and process all currently available commands.
        while (_eventQueue.TryDequeue(out var command, TimeSpan.Zero))
        {
            command.Socket.SendMoreFrame(Encoding.UTF8.GetBytes(command.Topic))
                          .SendSpanFrame(command.writer.WrittenMemory.Span);

            command.writer.Dispose();
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
    /// Schedules a command to be serialized and sent over a Socket via the poller thread.
    /// </summary>
    /// <param name="command">The command containing the Socket, topic, correlation ID, and payload to send.</param>
    public void Invoke(ScheduleEvent command) => _eventQueue.Enqueue(command);

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // Enqueue the Stop command to the poller's own queue. This is the correct way
                // to gracefully shut down the poller from an external thread.
                _poller.Stop();
                // Wait for the poller's thread to finish its work and exit.
                _thread.Join();

                // Now that the thread is stopped, it is safe to dispose of the poller and queues.
                _poller.Dispose();
                _actionQueue.Dispose();
                _eventQueue.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}