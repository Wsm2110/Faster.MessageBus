using CommunityToolkit.HighPerformance.Buffers;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Scope.Machine;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Implements the client-side logic for sending commands to other processes on the same machine.
/// This class orchestrates a "scatter-gather" pattern, broadcasting a single command to multiple
/// sockets and asynchronously collecting their individual responses.
/// </summary>
public class CommandScope(
    ICommandSocketManager socketManager,
    ICommandScheduler scheduler,
    ICommandSerializer serializer,
    ICommandReplyHandler commandReplyHandler) : ICommandScope
{
    private static readonly OperationCanceledException TimedOutException = new("The operation timed out.");
    private readonly ElasticPool _pendingReplyPool = new();
    private readonly ArrayPoolBufferWriter<byte> _writer = new();

    /// <summary>
    /// Broadcasts a command to all connected machine-local endpoints and returns an asynchronous stream of their responses.
    /// </summary>
    public async IAsyncEnumerable<TResponse> SendAsync<TResponse>(
        ICommand<TResponse> command,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Scatter the command and get the context for the pending replies.
        using var context = Scatter(command, timeout, ct);
        if (context.RequestCount == 0)
        {
            yield break;
        }

        // Gather Phase: Yield each response as it arrives, cleaning up resources immediately.
        for (int i = 0; i < context.RequestCount; i++)
        {
            var pending = context.Requests[i];
            try
            {
                ReadOnlyMemory<byte> respBytes = await pending.AsValueTask().ConfigureAwait(false);
                yield return serializer.Deserialize<TResponse>(respBytes);
            }
            finally
            {
                commandReplyHandler.TryUnregister(pending.CorrelationId);
                _pendingReplyPool.Return(pending);
            }
        }
        _writer.Clear();
    }

    /// <summary>
    /// Broadcasts a command without a specific response type and awaits their completion acknowledgments.
    /// </summary>
    public async Task SendAsync(ICommand command, TimeSpan timeout, CancellationToken ct = default)
    {
        // Scatter the command and get the context for the pending replies.
        using var context = Scatter(command, timeout, ct);
        if (context.RequestCount == 0)
        {
            return;
        }

        // Gather Phase: Await each operation and clean up its resources as it completes.
        for (int i = 0; i < context.RequestCount; i++)
        {
            var pending = context.Requests[i];
            try
            {
                await pending.AsValueTask().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // This is an expected result on timeout. We catch it to allow the loop
                // to continue and ensure all subsequent resources are still cleaned up.
            }
            finally
            {
                commandReplyHandler.TryUnregister(pending.CorrelationId);
                _pendingReplyPool.Return(pending);
            }
        }
        _writer.Clear();
    }

    /// <summary>
    /// Performs the "scatter" part of the operation: serializes the command, creates pending reply objects,
    /// dispatches them, and sets up a shared timeout. Returns a disposable context holding all resources.
    /// </summary>
    private ScatterContext Scatter<T>(T command, TimeSpan timeout, CancellationToken ct) where T : ICommand
    {
        var count = socketManager.Count; // Assuming Length property is available.
        var sockets = socketManager.Get(count);

        if (count == 0)
        {
            return ScatterContext.Empty;
        }

        // 1. RENT array from a pool instead of allocating a new one.
        var requests = ArrayPool<PendingReply<byte[]>>.Shared.Rent(count);

        // This section remains largely the same, but it's critical that `_writer`
        // is an efficient, pooled buffer writer to avoid allocations here.
        serializer.Serialize(command, _writer);

        // 2. GET the pre-calculated, cached topic hash. No allocation.
        var topic = WyHashHelper.Hash(command.GetType().Name);

        int requestIndex = 0;
        // 3. ITERATE over a span or non-allocating collection.
        foreach (var socketinfo in sockets)
        {
            var pending = _pendingReplyPool.Rent();
            commandReplyHandler.RegisterPending(pending);
            requests[requestIndex++] = pending;

            // 4. USE a struct for the command payload. This is a stack allocation.
            scheduler.Invoke(new ScheduleCommand
            {
                Socket = socketinfo.Socket,
                CorrelationId = pending.CorrelationId,
                Payload = _writer.WrittenMemory,
                Topic = topic
            });
        }

        // Creating a linked CTS is often an unavoidable allocation. For extreme needs,
        // a custom CancellationTokenSource pool could be implemented.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        // The state for the callback. A ValueTuple is a struct, so this is fine.
        var registrationState = (requests, requestIndex);
        var registration = linkedCts.Token.Register(static state =>
        {
            var (reqs, reqIndex) = ((PendingReply<byte[]>[], int))state!;
            for (int i = 0; i < reqIndex; i++)
            {
                var pending = reqs[i];
                // If the request is not completed, time it out.
                // Using TrySetException is often safer in concurrent scenarios.
                if (!pending.IsCompleted)
                {
                    pending.SetException(TimedOutException); // Assumes a static TimedOutException
                }
            }
        }, registrationState);

        // 5. RETURN a disposable context responsible for cleanup.
        return new ScatterContext(requests, requestIndex, linkedCts, registration);
    }

    /// <summary>
    /// A private disposable struct to manage the lifetime of resources created during the scatter operation.
    /// </summary>
    private readonly struct ScatterContext : IDisposable
    {
        public static ScatterContext Empty => new(Array.Empty<PendingReply<byte[]>>(), 0, null, default);

        public readonly PendingReply<byte[]>[] Requests;
        public readonly int RequestCount;
        private readonly CancellationTokenSource _linkedCts;
        private readonly CancellationTokenRegistration _timeoutRegistration;

        public ScatterContext(
            PendingReply<byte[]>[] requests,
            int requestCount,
            CancellationTokenSource linkedCts,
            CancellationTokenRegistration timeoutRegistration)
        {
            Requests = requests;
            RequestCount = requestCount;
            _linkedCts = linkedCts;
            _timeoutRegistration = timeoutRegistration;
        }

        public void Dispose()
        {
            // Unregister the callback to prevent it from running after disposal.
            _timeoutRegistration.Dispose();

            // Dispose the CancellationTokenSource to release its timer and resources.
            _linkedCts?.Dispose();

            // Return the array to the pool.
            if (Requests is not null)
            {
                // The `clearArray: true` is crucial to prevent the pool from holding onto
                // references to PendingReply objects, which could prevent them from being garbage collected.
                ArrayPool<PendingReply<byte[]>>.Shared.Return(Requests, clearArray: true);
            }
        }
    }
}