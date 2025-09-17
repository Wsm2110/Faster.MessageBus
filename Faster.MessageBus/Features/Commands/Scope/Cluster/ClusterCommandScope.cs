using CommunityToolkit.HighPerformance.Buffers;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
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
    private readonly ElasticPool _PendingReplyPool = new ElasticPool(1024);

    /// <summary>
    /// A reusable buffer writer that uses the ArrayPool to minimize memory allocations when serializing commands.
    /// It is a class field to be reused across multiple calls to the streaming SendAsync method.
    /// </summary>
    private readonly ArrayPoolBufferWriter<byte> _writer = new ArrayPoolBufferWriter<byte>();

    /// <summary>
    /// Broadcasts a command to all connected machine-local endpoints and returns an asynchronous stream of their responses.
    /// </summary>
    /// <typeparam name="TResponse">The expected type of the response objects.</typeparam>
    /// <param name="command">The command object containing the data to be sent.</param>
    /// <param name="timeout">The maximum total time to wait for all responses to arrive.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the entire operation.</param>
    /// <returns>An asynchronous stream (<see cref="IAsyncEnumerable{T}"/>) that yields each response as it is received.</returns>
    /// <remarks>
    /// This method is highly optimized and follows a specific workflow:
    /// 1. The command payload is serialized only once into a pooled buffer.
    /// 2. For each target Socket, a <see cref="PendingReply{TResult}"/> is rented from an object pool.
    /// 3. All send operations are scheduled on the thread-safe <see cref="ICommandScheduler"/>.
    /// 4. A single timeout/cancellation mechanism governs all outstanding requests.
    /// 5. Responses are awaited individually. As each response arrives, it is yielded to the caller,
    ///    and its associated resources are immediately returned to their respective pools.
    /// </remarks>
    public async IAsyncEnumerable<TResponse> SendAsync<TResponse>(
        ICommand<TResponse> command,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var count = socketManager.Count;
        if (count == 0)
        {
            yield break;
        }

        var requests = new PendingReply<byte[]>[count];
        serializer.Serialize(command, _writer);
        var topic = WyHashHelper.Hash(command.GetType().Name);

        // Scatter Phase: Dispatch a request to each Socket.
        int requestIndex = 0;
        foreach (var socketinfo in socketManager.Get(count))
        {
            var pending = _PendingReplyPool.Rent();
            commandReplyHandler.RegisterPending(pending);
            requests[requestIndex++] = pending;

            scheduler.Invoke(new ScheduleCommand
            {
                Socket = socketinfo.Socket,
                CorrelationId = pending.CorrelationId,
                Payload = _writer.WrittenMemory,
                Topic = WyHashHelper.Hash(command.GetType().Name)
            });
        }

        // Setup a single timeout/cancellation for all pending requests.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        using var _ = linkedCts.Token.Register(() =>
        {
            // Best-effort attempt to fault all outstanding requests on timeout.
            for (int i = 0; i < requestIndex; i++)
            {
                var pending = requests[i];
                if (!pending.IsCompleted)
                {
                    pending?.SetException(TimedOutException);
                }
            }
        });

        // Gather Phase: Await each reply and yield it as it arrives.
        for (int i = 0; i < requestIndex; i++)
        {
            var pending = requests[i];
            try
            {
                ReadOnlyMemory<byte> respBytes = await pending.AsValueTask().ConfigureAwait(false);
                yield return serializer.Deserialize<TResponse>(respBytes);
            }
            finally
            {
                // Crucially, unregister and return the pooled object inside the loop
                // to make it available for reuse as quickly as possible.
                commandReplyHandler.TryUnregister(pending.CorrelationId);
                _PendingReplyPool.Return(pending);
            }
        }

        // Final cleanup for the serialized payload buffer.
        _writer.Clear();
    }

    /// <summary>
    /// Broadcasts a command without a specific response type to all connected machine-local endpoints and awaits their completion acknowledgments.
    /// </summary>
    /// <param name="command">The command object to be sent.</param>
    /// <param name="timeout">The maximum time to wait for acknowledgments from all endpoints.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> that completes when all endpoints have acknowledged the command, the operation is cancelled, or the timeout is reached.</returns>
    public async Task SendAsync(ICommand command, TimeSpan timeout, CancellationToken ct = default)
    {
        var numSockets = socketManager.Count;
        if (numSockets == 0)
        {
            return;
        }

        var requests = new PendingReply<byte[]>[numSockets];
        // Using a local writer here as this method is less performance-critical than the streaming version.
        serializer.Serialize(command, _writer);

        var topic = WyHashHelper.Hash(command.GetType().Name);

        // Scatter Phase: Dispatch the command to each connected Socket.
        int count = 0;
        foreach (var socketInfo in socketManager.Get(numSockets))
        {
            var pending = _PendingReplyPool.Rent();
            commandReplyHandler.RegisterPending(pending);
            requests[count++] = pending;

            scheduler.Invoke(new ScheduleCommand
            {
                Socket = socketInfo.Socket,
                CorrelationId = pending.CorrelationId,
                Payload = _writer.WrittenMemory,
                Topic = topic
            });
        }

        // Setup a single timeout/cancellation for all pending requests.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);
        using var _ = linkedCts.Token.Register(() =>
        {
            // On timeout, attempt to fault any outstanding requests.
            for (int i = 0; i < count; i++)
            {
                requests[i]?.SetException(TimedOutException);
            }
        });

        // Gather Phase: Await completion of each request without processing a return value.
        for (int i = 0; i < count; i++)
        {
            var pending = requests[i];
            try
            {
                await pending.AsValueTask().ConfigureAwait(false);
            }
            finally
            {
                // Ensure resources are cleaned up even if the task faulted.
                commandReplyHandler.TryUnregister(pending.CorrelationId);
                _PendingReplyPool.Return(pending);
            }
        }

        _writer.Clear();
    }
}
