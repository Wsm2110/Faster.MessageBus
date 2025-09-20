using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using System.Buffers;

namespace Faster.MessageBus.Features.Commands.Scope.Local;

/// <summary>
/// Implements the client-side logic for sending commands to handlers within the current process (local scope).
/// This class orchestrates the serialization of the command, scheduling the send operation on a thread-safe scheduler,
/// and asynchronously waiting for and deserializing the reply.
/// </summary>
public class LocalCommandScope(
    ILocalSocketManager SocketManager,
    ICommandReplyHandler CommandReplyHandler,
    ICommandScheduler CommandScheduler,
    ICommandSerializer Serializer) : ILocalCommandScope
{
    /// <summary>
    /// A statically cached exception instance to avoid allocating a new exception for every timeout event.
    /// </summary>
    private static readonly OperationCanceledException TimedOutException = new("The operation timed out.");

    /// <summary>
    /// Asynchronously sends a command that expects a typed reply and waits for the response.
    /// </summary>
    /// <typeparam name="TResponse">The expected type of the response object.</typeparam>
    /// <param name="topic">The unique identifier for the command, used for routing.</param>
    /// <param name="command">The command object containing the data to be sent.</param>
    /// <param name="timeout">The maximum time to wait for a response.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation externally.</param>
    /// <returns>A task that resolves to the deserialized response object of type <typeparamref name="TResponse"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown if the operation times out or is canceled via the <paramref name="ct"/> token.</exception>
    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, TimeSpan timeout, CancellationToken ct = default)
    {
        var writer = new ArrayBufferWriter<byte>();
        var pendingReply = new PendingReply<byte[]>();

        // 1. Serialize the command payload into a buffer.
        Serializer.Serialize(command, writer);
        // 2. Register the pending reply so the system can correlate the response when it arrives.
        CommandReplyHandler.RegisterPending(pendingReply);

        // 3. Schedule the send operation to be executed on the thread-safe command scheduler.
        CommandScheduler.Invoke(new ScheduleCommand
        {
            Socket = SocketManager.LocalSocket,
            CorrelationId = pendingReply.CorrelationId,
            Payload = writer.WrittenMemory,
            Topic = WyHashHelper.Hash(command.GetType().Name),
        });

        // 4. Set up a linked cancellation token source to handle both timeout and external cancellation.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        // 5. Register a callback to fault the pending operation if cancellation occurs.
        using var _ = linkedCts.Token.Register(() =>
        {
            pendingReply.SetException(TimedOutException);
        });

        try
        {
            // 6. Asynchronously wait for the reply.
            ReadOnlyMemory<byte> respBytes = await pendingReply.AsValueTask().ConfigureAwait(false);

            // 7. Deserialize the response and return it.
            var response = Serializer.Deserialize<TResponse>(respBytes);
            return response;
        }
        finally
        {
            // 8. Crucially, clean up resources in a finally block to ensure it runs even on exception.
            CommandReplyHandler.TryUnregister(pendingReply.CorrelationId);
            writer.Clear(); // Return buffer to the pool.
        }
    }

    /// <summary>
    /// Asynchronously sends a command that does not return a value but awaits a confirmation of completion.
    /// </summary>
    /// <remarks>The method name "SendAsync" is unconventional; standard C# naming would be "SendAsync".</remarks>
    /// <param name="topic">The unique identifier for the command, used for routing.</param>
    /// <param name="command">The command object containing the data to be sent.</param>
    /// <param name="timeout">The maximum time to wait for a completion response.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation externally.</param>
    /// <returns>A task that completes when the command has been acknowledged by the receiver.</returns>
    /// <exception cref="OperationCanceledException">Thrown if the operation times out or is canceled via the <paramref name="ct"/> token.</exception>
    public async Task SendAsync(ICommand command, TimeSpan timeout, CancellationToken ct = default)
    {
        var writer = new ArrayBufferWriter<byte>();
        var pendingReply = new PendingReply<byte[]>();

        Serializer.Serialize(command, writer);
        CommandReplyHandler.RegisterPending(pendingReply);

        CommandScheduler.Invoke(new ScheduleCommand
        {
            Socket = SocketManager.LocalSocket,
            CorrelationId = pendingReply.CorrelationId,
            Payload = writer.WrittenMemory,
            Topic = WyHashHelper.Hash(command.GetType().Name),
        });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        using var _ = linkedCts.Token.Register(() =>
        {
            pendingReply.SetException(TimedOutException);
        });

        try
        {
            // Await the reply to ensure completion, but discard the result.
            await pendingReply.AsValueTask().ConfigureAwait(false);
        }
        finally
        {
            // Unregister and clean up resources.
            CommandReplyHandler.TryUnregister(pendingReply.CorrelationId);
            writer.Clear();
        }
    }
}