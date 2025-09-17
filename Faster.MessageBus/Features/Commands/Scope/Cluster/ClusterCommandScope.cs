using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Faster.MessageBus.Features.Commands.Scope.Cluster;

/// <summary>
/// Sends commands to a predefined group of machines in a cluster.
/// </summary>
public class ClusterCommandScope(
    IClusterSocketManager socketManager,
   [FromKeyedServices("clusterCommandScheduler")] ICommandScheduler scheduler,
    ICommandSerializer serializer,
    ICommandReplyHandler commandReplyHandler) : IClusterCommandScope
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
    /// Broadcasts a command to all connected machine-local endpoints and returns an asynchronous stream of their responses.
    /// </summary>
    /// <typeparam name="TResponse">The expected type of the response objects.</typeparam>
    /// <param name="topic">The unique identifier for the command, used for routing.</param>
    /// <param name="command">The command object containing the data to be sent.</param>
    /// <param name="timeout">The maximum total time to wait for all responses to arrive.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the entire operation.</param>
    /// <returns>An asynchronous stream (<see cref="IAsyncEnumerable{T}"/>) that yields each response as it is received.</returns>
    /// <remarks>
    /// This method is highly optimized and follows a specific workflow:
    /// 1. The command payload is serialized only once into a pooled buffer.
    /// 2. For each target socket, a <see cref="PendingReply{TResult}"/> is rented from an object pool.
    /// 3. All send operations are scheduled on the thread-safe <see cref="ICommandScheduler"/>.
    /// 4. A single timeout/cancellation mechanism governs all outstanding requests.
    /// 5. Responses are awaited individually. As each response arrives, it is yielded to the caller,
    ///    and its associated resources are immediately returned to their respective pools.
    /// </remarks>
    public async IAsyncEnumerable<TResponse> Send<TResponse>(
        ulong topic,
        ICommand<TResponse> command,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var numSockets = socketManager.Count;
        if (numSockets == 0) yield break;

        var requests = new PendingReply<byte[]>[numSockets];
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(command, writer);

        // Scatter Phase: Dispatch a request to each socket.
        int count = 0;
        foreach (var socket in socketManager.All)
        {
            var pending = _elasticPool.Rent();
            commandReplyHandler.RegisterPending(pending);
            requests[count++] = pending;

            scheduler.Invoke(new ScheduleCommand
            {
                Socket = socket,
                CorrelationId = pending.CorrelationId,
                Payload = writer.WrittenMemory,
                Topic = topic,
            });
        }

        // Setup a single timeout/cancellation for all pending requests.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        using var _ = linkedCts.Token.Register(() =>
        {
            // Best-effort attempt to fault all outstanding requests on timeout.
            for (int i = 0; i < count; i++)
            {
                requests[i]?.SetException(TimedOutException);
            }
        });

        // Gather Phase: Await each reply and yield it as it arrives.
        for (int i = 0; i < count; i++)
        {
            var pending = requests[i];
            try
            {
                ReadOnlyMemory<byte> respBytes = await pending.AsValueTask().ConfigureAwait(false);
                var response = serializer.Deserialize<TResponse>(respBytes);
                yield return response;
            }
            finally
            {
                // Crucially, unregister and return the pooled object inside the loop
                // to make it available for reuse as quickly as possible.
                commandReplyHandler.TryUnregister(pending.CorrelationId);
                _elasticPool.Return(pending);
            }
        }

        // Final cleanup for the serialized payload buffer.
        writer.Clear();
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