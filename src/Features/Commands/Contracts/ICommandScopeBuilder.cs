using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;

namespace Faster.MessageBus.Features.Commands.Contracts
{
    /// <summary>
    /// Defines the builder contract for configuring and dispatching fire-and-forget commands 
    /// that implement <see cref="ICommand"/>.
    /// Allows configuring execution options such as timeout and cancellation,
    /// and provides a fluent API for command dispatch.
    /// </summary>
    public interface ICommandScopeBuilder
    {
        /// <summary>
        /// Registers a handler to be invoked if the command dispatch operation times out.
        /// </summary>
        /// <param name="handler">The timeout handler, which receives the exception and the mesh context.</param>
        /// <returns>The same <see cref="ICommandScopeBuilder"/> instance for fluent chaining.</returns>
        CommandScopeBuilder OnTimeout(Action<Exception, MeshContext> handler);

        /// <summary>
        /// Sends the command in a fire-and-forget manner, respecting the configured options.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous send operation.</returns>
        Task SendAsync();

        /// <summary>
        /// Sets a cancellation token that can be used to cancel the command dispatch.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The same <see cref="ICommandScopeBuilder"/> instance for fluent chaining.</returns>
        CommandScopeBuilder WithCancellation(CancellationToken ct);

        /// <summary>
        /// Sets the maximum time to wait for the command dispatch before timing out.
        /// </summary>
        /// <param name="timeout">The timeout duration.</param>
        /// <returns>The same <see cref="ICommandScopeBuilder"/> instance for fluent chaining.</returns>
        CommandScopeBuilder WithTimeout(TimeSpan timeout);
    }

    /// <summary>
    /// Defines the builder contract for configuring and dispatching commands 
    /// that implement <see cref="ICommand{TResponse}"/> and produce results.
    /// Provides fluent APIs for both fire-and-forget send and scatter-gather style response streams.
    /// </summary>
    /// <typeparam name="TResponse">The type of response returned by the command.</typeparam>
    public interface ICommandScopeBuilder<TResponse>
    {
        /// <summary>
        /// Registers a handler to be invoked if the command dispatch operation times out.
        /// </summary>
        /// <param name="handler">The timeout handler, which receives the exception and the mesh context.</param>
        /// <returns>The same <see cref="ICommandScopeBuilder{TResponse}"/> instance for fluent chaining.</returns>
        CommandScopeBuilder<TResponse> OnTimeout(Action<Exception, MeshContext> handler);

        /// <summary>
        /// Streams the responses produced by the command in a scatter-gather style.
        /// </summary>
        /// <returns>An asynchronous sequence of <typeparamref name="TResponse"/> items.</returns>
        IAsyncEnumerable<TResponse> StreamAsync();

        /// <summary>
        /// Streams responses as <see cref="Result{T}"/> objects, 
        /// capturing both successes and errors for each participant in the scatter-gather.
        /// </summary>
        /// <returns>An asynchronous sequence of <see cref="Result{TResponse}"/> items.</returns>
        IAsyncEnumerable<Result<TResponse>> StreamResultAsync();

        /// <summary>
        /// Sets a cancellation token that can be used to cancel the command dispatch.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The same <see cref="ICommandScopeBuilder{TResponse}"/> instance for fluent chaining.</returns>
        CommandScopeBuilder<TResponse> WithCancellation(CancellationToken ct);

        /// <summary>
        /// Sets the maximum time to wait for the command dispatch before timing out.
        /// </summary>
        /// <param name="timeout">The timeout duration.</param>
        /// <returns>The same <see cref="ICommandScopeBuilder{TResponse}"/> instance for fluent chaining.</returns>
        CommandScopeBuilder<TResponse> WithTimeout(TimeSpan timeout);
    }
}
