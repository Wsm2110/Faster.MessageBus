using CommunityToolkit.HighPerformance.Buffers;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
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
    ICommandProcessor commandProcessor,
    ICommandSerializer serializer,
    ICommandReplyHandler commandReplyHandler) : ICommandScope
{
    // Cached exception instances for performance optimization to avoid frequent allocations
    private static readonly OperationCanceledException s_cachedOperationCanceledException = new("Operation was canceled due to timeout.");
    private readonly ElasticPool _pendingReplyPool = new();
    private readonly ArrayPoolBufferWriter<byte> _writer = new();

    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Sends a command to all listening endpoints on the local machine and returns an
    /// asynchronous stream of their successful responses.
    /// </summary>
    /// <remarks>
    /// This method is designed for "scatter-gather" scenarios where a single command can
    /// yield multiple replies. If any individual response fails or times out, that specific
    /// error is handled based on the presence of the <paramref name="OnException"/> callback.
    /// If <paramref name="OnException"/> is provided, the error is handled out-of-band, and
    /// the stream will yield a <c>default(TResponse)</c> for that failed item.
    /// If <paramref name="OnException"/> is <c>null</c>, individual errors will propagate
    /// as exceptions, potentially terminating the consumer's stream loop.
    /// <para>
    /// You can consume the stream of responses using an <c>await foreach</c> loop.
    /// <code>
    /// await foreach (var response in commandScope.StreamAsync(command, timeout, onTimeoutAction))
    /// {
    ///     // Process each successful response as it arrives
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <typeparam name="TResponse">The type of the response objects expected in the stream.</typeparam>
    /// <param name="command">The command object containing the data to be sent. Cannot be null.</param>
    /// <param name="timeout">The maximum duration to wait for each command response before it's considered timed out.</param>
    /// <param name="OnException">
    /// An optional callback that is invoked if an individual command response times out.
    /// The first parameter is the <see cref="Exception"/> (typically <see cref="OperationCanceledException"/>)
    /// representing the timeout, and the second is a string identifier (e.g., correlation ID) of the failed request.
    /// If this callback is provided, individual timeouts will not directly fault the stream but are handled out-of-band.
    /// If not provided, a timeout will throw an exception in the consumer's loop.
    /// </param>
    /// <param name="ct">
    /// A <see cref="CancellationToken"/> that can be used to cancel the entire operation externally.
    /// This token is automatically passed by <c>await foreach</c> loops.
    /// </param>
    /// <returns>
    /// An asynchronous stream (<see cref="IAsyncEnumerable{TResponse}"/>) that yields each successful response
    /// of type <typeparamref name="TResponse"/> as it is received from an endpoint on the machine.
    /// For timed-out or failed individual requests (when <paramref name="OnException"/> is provided),
    /// a <c>default(TResponse)</c> will be yielded.
    /// </returns>
    public async IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        ICommand<TResponse> command,
        TimeSpan timeout = default,
        Action<Exception, MeshInfo>? OnException = default,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (timeout == default)
        {
            timeout = DefaultTimeout;
        }

        // Scatter the command and get the context for the pending replies.
        using var context = Scatter(command, timeout, ct);
        if (context.RequestCount == 0)
        {
            yield break;
        }

        // Gather Phase: Process each response as it arrives, cleaning up resources immediately.
        for (int i = 0; i < context.RequestCount; i++)
        {
            var pending = context.Requests[i];
            TResponse val = default!; // Initialize with default to ensure it's always assigned
            try
            {
                ReadOnlyMemory<byte> respBytes = await pending.AsValueTask().ConfigureAwait(false);
                if (respBytes.Length == 0)
                {
                    OnException?.Invoke(new CommandProcessingException("A mesh server returned an empty response, indicating a processing error."), pending.Target);
                    continue;
                }
                val = serializer.Deserialize<TResponse>(respBytes);
            }
            catch (Exception e)
            {
                // Catch any other unexpected exceptions.
                OnException?.Invoke(e, pending.Target); // Use OnException for general exceptions too, or create a new callback.           
            }
            finally
            {
                commandReplyHandler.TryUnregister(pending.CorrelationId);
                _pendingReplyPool.Return(pending);
            }

            // Yield the response (which will be default(TResponse) if an error was handled out-of-band)
            yield return val;
        }
        _writer.Clear();
    }

    /// <summary>
    /// Sends a command to all listening endpoints on the local machine and awaits their completion,
    /// without returning any data in a stream.
    /// </summary>
    /// <remarks>
    /// This method is suitable for notification-style or "fire-and-await-completion" commands.
    /// The returned <see cref="Task"/> will complete successfully if all endpoints acknowledge
    /// the command within the specified timeout, or it will fault if an unhandled exception occurs
    /// or if the overall operation times out. Individual endpoint failures (if not handled via
    /// <paramref name="OnException"/>) will cause the overall <see cref="Task"/> to fault.
    /// </remarks>
    /// <param name="command">The command object containing the data to be sent. Cannot be null.</param>
    /// <param name="timeout">The maximum total time to wait for completion acknowledgments from all endpoints.</param>
    /// <param name="OnException">
    /// An optional callback that is invoked if an exception occurs during the processing of an individual command
    /// or a specific endpoint response. The first parameter is the <see cref="Exception"/> that occurred,
    /// and the second is a string identifier (e.g., correlation ID) of the failed request.
    /// If this callback is provided, individual exceptions are handled out-of-band, preventing them from
    /// directly faulting the returned <see cref="Task"/>. If not provided, exceptions will fault the task.
    /// </param>
    /// <param name="ct">
    /// An optional <see cref="CancellationToken"/> to cancel the entire operation externally.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that completes when all endpoints have acknowledged the command
    /// or the overall operation times out.
    /// </returns>
    public async Task SendAsync(ICommand command, TimeSpan timeout, Action<Exception, MeshInfo>? OnException = default, CancellationToken ct = default)
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
            catch (OperationCanceledException e)
            {
                OnException?.Invoke(e, pending.Target);
                // If OnException is handled, the loop continues.
            }
            catch (Exception e) // Catch any other exceptions for this item
            {
                OnException?.Invoke(e, pending.Target);
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
    /// Sends the specified command to multiple recipients and asynchronously yields each response as it arrives,
    /// explicitly capturing both successful responses and error/timeout information for each item.
    /// </summary>
    /// <remarks>
    /// This method is designed for "scatter-gather" scenarios. Each response is yielded as soon as it is available.
    /// If a response is not received within the specified <paramref name="timeout"/> or the operation
    /// is canceled, the corresponding <see cref="Result{TResponse}"/> will contain the error or
    /// cancellation information. This approach ensures robust, item-by-item error handling without
    /// interrupting the entire stream. Resources are released promptly after each response is processed.
    /// <para>
    /// **Example Usage:**
    /// <code>
    /// public async Task ProcessResponses(ICommandScope commandScope, MyCommand myCommand)
    /// {
    ///     var timeout = TimeSpan.FromSeconds(5);
    ///     await foreach (var result in commandScope.StreamResultAsync&lt;MyResponse&gt;(myCommand, timeout))
    ///     {
    ///         if (result.IsSuccess)
    ///         {
    ///             // Successfully received a response
    ///             MyResponse response = (MyResponse)result; // Explicit cast to get the value
    ///             Console.WriteLine($"Received successful response: {response.Data}");
    ///             // Update UI, log success, etc.
    ///         }
    ///         else
    ///         {
    ///             // An individual request failed or timed out
    ///             if (result.Error is OperationCanceledException)
    ///             {
    ///                 Console.WriteLine($"A request timed out or was canceled: {result.Error.Message}");
    ///                 // Update UI to indicate a specific part of the operation failed due to timeout
    ///             }
    ///             else
    ///             {
    ///                 Console.WriteLine($"A request failed with an unexpected error: {result.Error.Message}");
    ///                 // Update UI with a general error message for this specific item
    ///             }
    ///         }
    ///     }
    ///     Console.WriteLine("All command results processed (or some handled gracefully).");
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <typeparam name="TResponse">The type of the response expected from each recipient.</typeparam>
    /// <param name="command">The command to be sent to all recipients. Cannot be null.</param>
    /// <param name="timeout">The maximum duration to wait for each response before timing out.</param>
    /// <param name="ct">
    /// A <see cref="CancellationToken"/> that can be used to cancel the entire operation externally.
    /// This token is automatically passed by <c>await foreach</c> loops.
    /// </param>
    /// <returns>
    /// An asynchronous sequence of <see cref="Result{TResponse}"/> containing either successful
    /// responses or detailed error information for each attempt. The sequence is empty if no
    /// requests are sent.
    /// </returns>
    public async IAsyncEnumerable<Result<TResponse>> StreamResultAsync<TResponse>(
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
            Result<TResponse> val; // Declare without default to ensure it's assigned in try/catch
            try
            {
                ReadOnlyMemory<byte> respBytes = await pending.AsValueTask().ConfigureAwait(false);
                if (respBytes.Length == 0)
                {
                    val = (Result<TResponse>)new CommandProcessingException("A mesh server returned an empty response, indicating a processing error.");
                    continue;
                }

                TResponse response = serializer.Deserialize<TResponse>(respBytes);
                val = response; // Implicit conversion from TResponse to Result<TResponse>.Success
            }
            catch (OperationCanceledException)
            {
                // Use cached exception for performance
                val = (Result<TResponse>)s_cachedOperationCanceledException; // Explicit conversion from Exception to Result<TResponse>.Failure
            }
            catch (Exception e)
            {
                // Catch any other unexpected exceptions
                val = (Result<TResponse>)e; // Explicit conversion from Exception to Result<TResponse>.Failure
            }
            finally
            {
                commandReplyHandler.TryUnregister(pending.CorrelationId);
                _pendingReplyPool.Return(pending);
            }

            yield return val;
        }
        _writer.Clear();
    }

    /// <summary>
    /// Performs the "scatter" part of the operation: serializes the command, creates pending reply objects,
    /// dispatches them, and sets up a shared timeout. Returns a disposable context holding all resources.
    /// </summary>
    /// <typeparam name="T">The type of the command being sent.</typeparam>
    /// <param name="command">The command instance to serialize and dispatch.</param>
    /// <param name="timeout">The duration after which the linked cancellation token will be canceled.</param>
    /// <param name="ct">An external cancellation token to link with the timeout token.</param>
    /// <returns>A <see cref="ScatterContext"/> containing the dispatched requests and cancellation resources.</returns>
    private ScatterContext Scatter<T>(T command, TimeSpan timeout, CancellationToken ct) where T : ICommand
    {
        var count = commandProcessor.Count;
        var sockets = commandProcessor.Get(count);

        if (count == 0)
        {
            return ScatterContext.Empty;
        }

        // Rent an array from a pool to store pending replies, avoiding allocations.
        var requests = ArrayPool<PendingReply<byte[]>>.Shared.Rent(count);

        // Serialize the command into a pooled buffer writer to avoid heap allocations.
        _writer.Clear(); // Ensure writer is clear before serializing
        serializer.Serialize(command, _writer);

        // Get the pre-calculated, cached topic hash for efficient routing.
        var topic = WyHash.Hash(command.GetType().Name);

        int requestIndex = 0;
        // Iterate over the available sockets (or endpoints).
        foreach (var socketinfo in sockets)
        {
            var pending = _pendingReplyPool.Rent(); // Rent a PendingReply object from a pool
            commandReplyHandler.RegisterPending(pending); // Register to receive its reply
            requests[requestIndex++] = pending; // Store it in our rented array

            pending.Target = socketinfo.Item2.Info;

            // Schedule the command for processing, using a struct for payload to reduce allocations.
            commandProcessor.ScheduleCommand(new ScheduleCommand
            {
                Socket = socketinfo.Item2.Socket,
                CorrelationId = pending.CorrelationId,
                Payload = _writer.WrittenMemory,
                Topic = topic
            });
        }

        // Create a CancellationTokenSource linked to the external token and a timeout.
        // This is often an unavoidable allocation.
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        // Register a callback to fault pending requests if the cancellation token is triggered (timeout or external cancel).
        var registrationState = (requests, requestIndex);
        var registration = linkedCts.Token.Register(state =>
        {
            var (reqs, reqIndex) = ((PendingReply<byte[]>[], int))state!;
            for (int i = 0; i < reqIndex; i++)
            {
                var pending = reqs[i];
                // If the request is not yet completed, set it to a canceled state.
                if (!pending.IsCompleted)
                {
                    // Use a cached OperationCanceledException for performance.
                    pending.SetException(s_cachedOperationCanceledException);
                }
            }
        }, registrationState);

        // Return a disposable context responsible for managing these resources.
        return new ScatterContext(requests, requestIndex, linkedCts, registration);
    }

    /// <summary>
    /// A private disposable struct to manage the lifetime of resources created during the scatter operation.
    /// This ensures proper cleanup of pooled arrays and cancellation tokens.
    /// </summary>
    private readonly struct ScatterContext : IDisposable
    {
        /// <summary>
        /// Represents an empty <see cref="ScatterContext"/> for cases where no requests are sent.
        /// </summary>
        public static ScatterContext Empty => new(Array.Empty<PendingReply<byte[]>>(), 0, null!, default); // Use null! for _linkedCts if it can be null

        /// <summary>
        /// The array of <see cref="PendingReply{T}"/> objects for the dispatched commands.
        /// </summary>
        public readonly PendingReply<byte[]>[] Requests;

        /// <summary>
        /// The actual number of valid requests stored in the <see cref="Requests"/> array.
        /// </summary>
        public readonly int RequestCount;

        private readonly CancellationTokenSource? _linkedCts; // Make nullable as it can be null for Empty context
        private readonly CancellationTokenRegistration _timeoutRegistration;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterContext"/> struct.
        /// </summary>
        /// <param name="requests">The array of pending reply objects.</param>
        /// <param name="requestCount">The number of valid requests in the array.</param>
        /// <param name="linkedCts">The linked <see cref="CancellationTokenSource"/> for managing timeout and external cancellation.</param>
        /// <param name="timeoutRegistration">The registration for the timeout callback.</param>
        public ScatterContext(
            PendingReply<byte[]>[] requests,
            int requestCount,
            CancellationTokenSource? linkedCts, // Parameter is nullable
            CancellationTokenRegistration timeoutRegistration)
        {
            Requests = requests;
            RequestCount = requestCount;
            _linkedCts = linkedCts;
            _timeoutRegistration = timeoutRegistration;
        }

        /// <summary>
        /// Disposes the resources held by this context, including unregistering the timeout callback,
        /// disposing the <see cref="CancellationTokenSource"/>, and returning the request array to the pool.
        /// </summary>
        public void Dispose()
        {
            // Unregister the callback to prevent it from running after disposal.
            _timeoutRegistration.Dispose();

            // Dispose the CancellationTokenSource to release its timer and resources.
            _linkedCts?.Dispose();

            // Return the array to the pool if it was rented.
            if (Requests is not null)
            {
                // The `clearArray: true` is crucial to prevent the pool from holding onto
                // references to PendingReply objects, which could prevent them from being garbage collected.
                ArrayPool<PendingReply<byte[]>>.Shared.Return(Requests, clearArray: true);
            }
        }
    }
}