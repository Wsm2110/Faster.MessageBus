using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Scope.Machine;
using Faster.MessageBus.Features.Commands.Shared;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Faster.MessageBus.Features.Commands.Scope.Network;

/// <summary>
/// Broadcasts commands to all available machines/services on the network.
/// </summary>
public class NetworkCommandScope(
    INetworkSocketManager socketManager,
    [FromKeyedServices("networkCommandScheduler")] ICommandScheduler scheduler,
    ICommandSerializer serializer,
    ICommandReplyHandler commandReplyHandler) : INetworkCommandScope
{
    /// <summary>
    /// A statically cached exception instance to avoid allocating a new exception for every timeout event.
    /// </summary>
    private static readonly OperationCanceledException TimedOutException = new("The operation timed out.");

    /// <summary>
    /// A high-performance object pool for reusing <see cref="PendingReply{TResult}"/> objects. This significantly
    /// reduces memory allocations and GC pressure during high-throughput scatter-gather operations.
    /// </summary>
    private readonly ElasticPool _elasticPool = new ElasticPool(1024);

    /// <summary>
    /// Sends a command to all connected sockets using a scatter-gather pattern and asynchronously streams back the replies.
    /// This method does not wait for all replies before returning; instead, it yields each reply as soon as it is received.
    /// </summary>
    /// <typeparam name="TResponse">The expected data type of the response.</typeparam>
    /// <param name="topic">The topic identifier used for routing the command.</param>
    /// <param name="command">The command object to be serialized and sent to all recipients.</param>
    /// <param name="timeout">The maximum duration to wait for all replies. After this period, any outstanding requests are faulted.</param>
    /// <param name="ct">A cancellation token that can be used to abort the send/receive operation.</param>
    /// <returns>An asynchronous stream (`IAsyncEnumerable&lt;TResponse&gt;`) that yields responses as they arrive.</returns>
    public async IAsyncEnumerable<TResponse> Send<TResponse>(
        ulong topic,
        ICommand<TResponse> command,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Get the number of connected sockets to determine the scope of the operation.
        var numSockets = socketManager.Count;
        // If there are no connected sockets, there is nothing to do. Exit immediately.
        if (numSockets == 0) yield break;

        // Pre-allocate an array to hold references to all pending reply objects.
        var requests = new PendingReply<byte[]>[numSockets];

        // Use ArrayBufferWriter for efficient, low-allocation serialization of the command payload.
        // The command is serialized only once and the resulting memory is sent to all sockets.
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(command, writer);

        // --- Scatter Phase ---
        // Dispatch the serialized command to every connected socket.
        int count = 0;
        // Note: This assumes socketManager.All returns a thread-safe snapshot or is accessed safely.
        foreach (var socket in socketManager.All)
        {
            // Rent a reusable PendingReply object from a pool to avoid GC allocations.
            var pending = _elasticPool.Rent();
            // Register the pending request so that incoming replies can be matched by correlation ID.
            commandReplyHandler.RegisterPending(pending);
            // Store the pending object to await its result later in the gather phase.
            requests[count++] = pending;

            // Schedule the actual send operation to run on the socket's dedicated scheduler thread.
            // This ensures all socket operations are thread-safe without locks.
            scheduler.Invoke(new ScheduleCommand
            {
                Socket = socket,
                CorrelationId = pending.CorrelationId,
                Payload = writer.WrittenMemory,
                Topic = topic,
            });
        }

        // --- Timeout and Cancellation Setup ---
        // Create a linked CancellationTokenSource that combines the external token and the timeout.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        // Register a callback to fire upon cancellation (either from timeout or the external token).
        using var _ = linkedCts.Token.Register(() =>
        {
            // This is a best-effort attempt to fault all outstanding requests when cancellation occurs.
            // It unblocks any awaiters in the gather phase, preventing them from hanging.
            for (int i = 0; i < count; i++)
            {
                requests[i]?.SetException(TimedOutException);
            }
        });

        // --- Gather Phase ---
        // Await each reply individually and yield it as it arrives.
        for (int i = 0; i < count; i++)
        {
            var pending = requests[i];
            try
            {
                // Asynchronously wait for a single response to be received.
                // This will unblock as soon as the corresponding reply arrives or a timeout/cancellation occurs.
                ReadOnlyMemory<byte> respBytes = await pending.AsValueTask().ConfigureAwait(false);

                // Deserialize the raw byte response into the target type.
                var response = serializer.Deserialize<TResponse>(respBytes);

                // Yield the deserialized response to the consumer of the async stream.
                yield return response;
            }
            finally
            {
                // This block is crucial for resource management. It executes whether the await
                // succeeded, failed, or was cancelled.

                // Unregister the completed or faulted request from the reply handler.
                commandReplyHandler.TryUnregister(pending.CorrelationId);

                // Return the pooled object back to the pool, making it available for reuse immediately.
                // Doing this inside the loop ensures prompt cleanup.
                _elasticPool.Return(pending);
            }
        }
    }
    /// <summary>
    /// Broadcasts a command to all listening endpoints on the local machine and awaits their completion, without returning any data.
    /// </summary>
    /// <remarks>The method name "SendAsync" is unconventional; standard C# naming would be "SendAsync".</remarks>
    /// <param name="topic">The unique identifier for the command, used for routing.</param>
    /// <param name="command">The command object containing the data to be sent.</param>
    /// <param name="timeout">The maximum time to wait for completion acknowledgments from all endpoints.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation externally.</param>
    /// <returns>A <see cref="Task"/> that completes when all endpoints have acknowledged the command or the operation times out.</returns>
    public async Task SendASync(ulong topic, ICommand command, TimeSpan timeout, CancellationToken ct = default)
    {
        var numSockets = socketManager.Count;
        if (numSockets == 0) return;

        var requests = new PendingReply<byte[]>[numSockets];
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(command, writer);

        // Scatter Phase
        int count = 0;
        foreach (var socket in socketManager.All)
        {
            var pending = _elasticPool.Rent();
            commandReplyHandler.RegisterPending(pending);
            requests[count++] = pending;

            scheduler.Invoke(new ScheduleCommand // Assuming 'ScheduleCommand' is a typo for 'ProcessCommand'
            {
                Socket = socket,
                CorrelationId = pending.CorrelationId,
                Payload = writer.WrittenMemory,
                Topic = topic,
            });
        }

        // Timeout/Cancellation
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);
        using var _ = linkedCts.Token.Register(() =>
        {
            for (int i = 0; i < requests.Length; i++)
            {
                requests[i]?.SetException(TimedOutException);
            }
        });

        // Gather Phase (awaiting completion without yielding data)
        for (int i = 0; i < count; i++)
        {
            var pending = requests[i];
            try
            {
                await pending.AsValueTask().ConfigureAwait(false);
            }
            finally
            {
                commandReplyHandler.TryUnregister(pending.CorrelationId);
                _elasticPool.Return(pending);
            }
        }

        writer.Clear();
    }
}


