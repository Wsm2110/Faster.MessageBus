using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;

namespace Faster.MessageBus.Features.Commands.Contracts;

/// <summary>
/// Defines the contract for sending commands to one or more endpoints within the same machine,
/// but outside of the current process (Inter-Process Communication). This scope manages the
/// "scatter-gather" pattern for command execution and response collection.
/// </summary>
public interface ICommandScope
{
    /// <summary>
    /// Prepares command for dispatch, returning a scope builder
    /// that allows configuration of timeouts and cancellation.
    /// </summary>
    /// <param name="command">The command to be dispatched.</param>
    /// <returns>
    /// An <see cref="ICommandScopeBuilder"/> that provides a fluent API for configuring
    /// and sending the command without expecting a response.
    /// </returns>
    ICommandScopeBuilder Prepare(ICommand command);

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
    ICommandScopeBuilder<TResponse> Prepare<TResponse>(ICommand<TResponse> command);

    /// <summary>
    /// Sends a command to all listening endpoints on the local machine and returns an
    /// asynchronous stream of their successful responses.
    /// </summary>
    /// <remarks>
    /// This method is designed for "scatter-gather" scenarios where a single command can
    /// yield multiple replies. If any individual response fails, times out, or encounters
    /// an exception, that specific error will be handled either by the <paramref name="OnException"/>
    /// callback (if provided) or by allowing the exception to propagate directly to the consumer
    /// of the <see cref="IAsyncEnumerable{TResponse}"/> stream, potentially terminating the stream.
    /// <para>
    /// You can consume the stream of responses using an <c>await foreach</c> loop.
    /// <code>
    /// await foreach (var response in commandScope.StreamAsync(command, timeout))
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
    /// representing the timeout, and the second is a string identifier or correlation ID if available.
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
    /// </returns>
    IAsyncEnumerable<TResponse> StreamAsync<TResponse>(ICommand<TResponse> command, TimeSpan timeout = default, Action<Exception, MeshContext>? OnException = default, CancellationToken ct = default);

    /// <summary>
    /// Sends the specified command asynchronously and returns an asynchronous sequence of results,
    /// explicitly capturing both successful responses and error/timeout information for each item.
    /// </summary>
    /// <remarks>
    /// This method ensures that errors and timeouts for individual responses are captured within
    /// the <see cref="Result{TResponse}"/> sequence rather than being thrown as exceptions.
    /// This design allows for robust, item-by-item error handling in consuming code without
    /// interrupting the entire stream. Consumers should check <see cref="Result{TResponse}.IsSuccess"/>
    /// for each item.
    /// </remarks>
    /// <typeparam name="TResponse">The type of the response expected from the command.</typeparam>
    /// <param name="command">The command to be sent. Cannot be null.</param>
    /// <param name="timeout">The maximum duration to wait for each command response before timing out.</param>
    /// <param name="ct">
    /// A <see cref="CancellationToken"/> that can be used to cancel the entire operation externally.
    /// This token is automatically passed by <c>await foreach</c> loops.
    /// </param>
    /// <returns>
    /// An asynchronous sequence of <see cref="Result{TResponse}"/> containing either successful
    /// responses or detailed error information for each attempt.
    /// </returns>
    IAsyncEnumerable<Result<TResponse>> StreamResultAsync<TResponse>(ICommand<TResponse> command, TimeSpan timeout = default, CancellationToken ct = default);

    /// <summary>
    /// Sends a command to all listening endpoints on the local machine and awaits their completion,
    /// without returning any data in a stream.
    /// </summary>
    /// <remarks>
    /// This method is suitable for notification-style or "fire-and-await-completion" commands.
    /// The returned <see cref="Task"/> will complete successfully if all endpoints acknowledge
    /// the command within the specified timeout, or it will fault if an exception occurs
    /// or if the entire operation times out. Individual endpoint failures (if not handled internally)
    /// will cause the overall <see cref="Task"/> to fault.
    /// </remarks>
    /// <param name="command">The command object containing the data to be sent. Cannot be null.</param>
    /// <param name="timeout">The maximum total time to wait for completion acknowledgments from all endpoints.</param>
    /// <param name="OnException">
    /// An optional callback that is invoked if an exception occurs during the processing of an individual command
    /// or a specific endpoint response. The first parameter is the <see cref="Exception"/> that occurred,
    /// and the second is a string identifier or correlation ID if available.
    /// If this callback is provided, individual exceptions will be handled out-of-band, preventing them from
    /// directly faulting the returned <see cref="Task"/>. If not provided, exceptions will fault the task.
    /// </param>
    /// <param name="ct">
    /// An optional <see cref="CancellationToken"/> to cancel the entire operation externally.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that completes when all endpoints have acknowledged the command
    /// or the operation times out.
    /// </returns>
    Task SendAsync(ICommand command, TimeSpan timeout = default, Action<Exception, MeshContext>? OnException = default, CancellationToken ct = default);

}