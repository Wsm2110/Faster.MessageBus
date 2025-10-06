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
    ICommandSocketManager commandSocketManager,
    ICommandSerializer serializer,
    ICommandResponseHandler commandResponseHandler) : ICommandScope
{
    // Cached exception instances for performance optimization to avoid frequent allocations
    private static readonly OperationCanceledException s_cachedOperationCanceledException = new("Operation was canceled due to timeout.");

    private readonly ObjectPool<PendingReply<byte[]>> _pool = new ObjectPool<PendingReply<byte[]>>(1024);

    /// <summary>
    /// Prepares a strongly-typed command for dispatch, returning a scope builder
    /// that allows configuration of timeouts, cancellation, and scatter-gather response streaming.
    /// </summary>
    /// <typeparam name="TResponse">The type of the expected response from the command.</typeparam>
    /// <param name="command">The command to be dispatched.</param>
    /// <returns>
    /// An <see cref="ICommandScopeBuilder{TResponse}"/> that provides a fluent API for configuring
    /// and sending the command.
    /// </returns>
    public ICommandScopeBuilder<TResponse> Prepare<TResponse>(ICommand<TResponse> command)
    {
        return new CommandScopeBuilder<TResponse>(this, command);
    }

    /// <summary>
    /// Prepares command for dispatch, returning a scope builder
    /// that allows configuration of timeouts and cancellation.
    /// </summary>
    /// <param name="command">The command to be dispatched.</param>
    /// <returns>
    /// An <see cref="ICommandScopeBuilder"/> that provides a fluent API for configuring
    /// and sending the command without expecting a response.
    /// </returns>
    public ICommandScopeBuilder Prepare(ICommand command)
    {
        return new CommandScopeBuilder(this, command);
    }

    /// <summary>
    /// Sends a command to all listening endpoints on the local machine and returns an
    /// asynchronous stream of their successful responses.
    /// </summary>
    /// <remarks>
    /// This method is designed for "scatter-gather" scenarios where a single command can
    /// yield multiple replies. If any individual response fails or times out, that specific
    /// error is handled based on the presence of the <paramref name="OnTimeout"/> callback.
    /// If <paramref name="OnTimeout"/> is provided, the error is handled out-of-band, and
    /// the stream will yield a <c>default(TResponse)</c> for that failed item.
    /// If <paramref name="OnTimeout"/> is <c>null</c>, individual errors will propagate
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
    /// <param name="OnTimeout">
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
    /// For timed-out or failed individual requests (when <paramref name="OnTimeout"/> is provided),
    /// a <c>default(TResponse)</c> will be yielded.
    /// </returns>
    public async IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        ICommand<TResponse> command,
        TimeSpan timeout,
        Action<Exception, MeshContext>? OnTimeout = default,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
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
                // If the response is empty, continue to the next iteration.
                // Note that an empty response is valid for commands without a matching commandhandler.
                if (respBytes.Length == 0)
                {
                    continue;
                }

                val = serializer.Deserialize<TResponse>(respBytes);
            }
            //catch (OperationCanceledException e)
            //{
            //    // Catch any other unexpected exceptions.
            //    OnTimeout?.Invoke(e, pending.Target); // Use OnTimeout for general exceptions too, or create a new callback.           
            //}
            finally
            {
                commandResponseHandler.TryUnregister(pending.CorrelationId);
                pending.Reset();
                _pool.Return(pending);
            }

            // Yield the response (which will be default(TResponse) if an error was handled out-of-band)
            yield return val;
        }
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
    /// <paramref name="OnTimeout"/>) will cause the overall <see cref="Task"/> to fault.
    /// </remarks>
    /// <param name="command">The command object containing the data to be sent. Cannot be null.</param>
    /// <param name="timeout">The maximum total time to wait for completion acknowledgments from all endpoints.</param>
    /// <param name="OnTimeout">
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
    public async Task SendAsync(ICommand command, TimeSpan timeout, Action<Exception, MeshContext>? OnTimeout = default, CancellationToken ct = default)
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
                OnTimeout?.Invoke(e, pending.Target);
            }
            finally
            {
                commandResponseHandler.TryUnregister(pending.CorrelationId);
                pending.Reset();
                _pool.Return(pending);
            }
        }
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
                commandResponseHandler.TryUnregister(pending.CorrelationId);
                pending.Reset();
                _pool.Return(pending);
            }

            yield return val;
        }
    }

    private long _counter = 0;

    /// <summary>
    /// Performs the "scatter" operation by dispatching a command to multiple endpoints,
    /// setting up pending replies, serializing the command, and managing cancellation and timeout.
    /// </summary>
    /// <typeparam name="T">The type of the command being sent. Must implement <see cref="ICommand"/>.</typeparam>
    /// <param name="command">The command instance to serialize and dispatch.</param>
    /// <param name="timeout">The duration after which the linked cancellation token will cancel pending requests.</param>
    /// <param name="ct">An external <see cref="CancellationToken"/> to link with the timeout.</param>
    /// <returns>
    /// A <see cref="ScatterContext"/> containing all pending requests, the linked cancellation token,
    /// and the timeout registration. Disposing the context will clean up resources.
    /// </returns>
    private ScatterContext Scatter<T>(T command, TimeSpan timeout, CancellationToken ct) where T : ICommand
    {
        var count = commandSocketManager.Count;
        if (count == 0) return ScatterContext.Empty;

        // Compute topic hash for the command type
        var topic = WyHash.Hash(command.GetType().Name);

        // Get eligible sockets for this command
        var sockets = commandSocketManager.Get(count, topic);

        // Rent array from pool to hold PendingReply objects
        var requests = new PendingReply<byte[]>[count];

        ArrayPoolBufferWriter<byte> _writer = new();

        serializer.Serialize(command, _writer);

        Interlocked.Increment(ref _counter);

        int requestIndex = 0;
        foreach (var socketInfo in sockets)
        {
            var pending = _pool.Rent();       // Rent a PendingReply object
            commandResponseHandler.RegisterPending(pending); // Register to receive its reply
            requests[requestIndex++] = pending;           // Store in the rented array
            pending.Target = socketInfo.Item2.Info;

            // Schedule the command to be sent over the socket
            commandSocketManager.ScheduleCommand(new ScheduleCommand
            {
                Socket = socketInfo.Item2.Socket,
                CorrelationId = pending.CorrelationId,
                Payload = _writer.WrittenMemory,
                Topic = topic
            });
        }

        // Link external cancellation with timeout to cancel pending requests
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout);

        // Register static callback to fault pending requests on cancellation/timeout       
        var registration = linkedCts.Token.Register(() =>
        {
            for (int i = 0; i < count; i++)
            {
                ref var pending = ref requests[i];
                if (!pending.IsCompleted)
                {
                    pending.SetException(s_cachedOperationCanceledException);
                }
            }
        });

        return new ScatterContext(requests, requestIndex, linkedCts, registration, _writer);
    }

    /// <summary>
    /// Represents the disposable context for a scatter operation,
    /// including pending requests, cancellation token, and timeout registration.
    /// </summary>
    private readonly struct ScatterContext : IDisposable
    {
        /// <summary>
        /// Returns an empty <see cref="ScatterContext"/> used when no requests are sent.
        /// </summary>
        public static ScatterContext Empty => new(Array.Empty<PendingReply<byte[]>>(), 0, null, default, null);

        public readonly PendingReply<byte[]>[] Requests;
        public readonly int RequestCount;
        private readonly CancellationTokenSource? _linkedCts;
        private readonly CancellationTokenRegistration _timeoutRegistration;
        private readonly ArrayPoolBufferWriter<byte> _writer;

        public ScatterContext(
            PendingReply<byte[]>[] requests,
            int requestCount,
            CancellationTokenSource? linkedCts,
            CancellationTokenRegistration timeoutRegistration,
            ArrayPoolBufferWriter<byte> writer)
        {
            Requests = requests;
            RequestCount = requestCount;
            _linkedCts = linkedCts;
            _timeoutRegistration = timeoutRegistration;
            _writer = writer;
        }

        /// <summary>
        /// Disposes the context by unregistering the timeout callback, disposing
        /// the linked cancellation token, and returning the request array to the pool.
        /// </summary>
        public void Dispose()
        {
            // Dispose registration first to prevent callback firing
            _timeoutRegistration.Dispose();

            // Dispose linked CTS
            _linkedCts?.Dispose();

            _writer?.Dispose();
            // Return rented array to pool          
            // ArrayPool<PendingReply<byte[]>>.Shared.Return(Requests, clearArray: true);
        }
    }

}