using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Provides a fluent API for configuring and dispatching a command
/// without expecting a response.
/// </summary>
public sealed class CommandScopeBuilder : ICommandScopeBuilder
{
    private readonly ICommandScope _scope;
    private readonly ICommand _command;

    private TimeSpan _timeout = TimeSpan.FromSeconds(1);
    private Action<Exception, MeshContext>? _onTimeout;
    private CancellationToken _ct = default;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandScopeBuilder"/> class.
    /// </summary>
    /// <param name="scope">The scope responsible for executing the command.</param>
    /// <param name="command">The command to be executed.</param>
    public CommandScopeBuilder(ICommandScope scope, ICommand command)
    {
        _scope = scope;
        _command = command;
    }

    /// <summary>
    /// Specifies the timeout for executing the command.
    /// </summary>
    /// <param name="timeout">The maximum duration allowed before timing out.</param>
    /// <returns>The same <see cref="CommandScopeBuilder"/> instance for chaining.</returns>
    public CommandScopeBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Specifies a handler to invoke if the command execution times out.
    /// </summary>
    /// <param name="handler">
    /// The callback that receives the timeout <see cref="Exception"/> and
    /// the <see cref="MeshContext"/> in which the timeout occurred.
    /// </param>
    /// <returns>The same <see cref="CommandScopeBuilder"/> instance for chaining.</returns>
    public CommandScopeBuilder OnTimeout(Action<Exception, MeshContext> handler)
    {
        _onTimeout = handler;
        return this;
    }

    /// <summary>
    /// Adds a cancellation token that can abort the command execution.
    /// </summary>
    /// <param name="ct">The cancellation token to observe.</param>
    /// <returns>The same <see cref="CommandScopeBuilder"/> instance for chaining.</returns>
    public CommandScopeBuilder WithCancellation(CancellationToken ct)
    {
        _ct = ct;
        return this;
    }

    /// <summary>
    /// Sends the command asynchronously without expecting a response.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the send operation.</returns>
    public Task SendAsync() => _scope.SendAsync(_command, _timeout, _onTimeout, _ct);
}

/// <summary>
/// Provides a fluent API for configuring and dispatching a command
/// that expects a response or a stream of responses.
/// </summary>
/// <typeparam name="TResponse">The expected response type of the command.</typeparam>
public sealed class CommandScopeBuilder<TResponse> : ICommandScopeBuilder<TResponse>
{
    private readonly ICommandScope _scope;
    private readonly ICommand<TResponse> _command;

    private TimeSpan _timeout = TimeSpan.FromSeconds(1);
    private Action<Exception, MeshContext>? _onTimeout;
    private CancellationToken _ct = default;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandScopeBuilder{TResponse}"/> class.
    /// </summary>
    /// <param name="scope">The scope responsible for executing the command.</param>
    /// <param name="command">The command to be executed.</param>
    public CommandScopeBuilder(ICommandScope scope, ICommand<TResponse> command)
    {
        _scope = scope;
        _command = command;
    }

    /// <summary>
    /// Specifies the timeout for executing the command.
    /// </summary>
    /// <param name="timeout">The maximum duration allowed before timing out.</param>
    /// <returns>The same <see cref="CommandScopeBuilder{TResponse}"/> instance for chaining.</returns>
    public CommandScopeBuilder<TResponse> WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Specifies a handler to invoke if the command execution times out.
    /// </summary>
    /// <param name="handler">
    /// The callback that receives the timeout <see cref="Exception"/> and
    /// the <see cref="MeshContext"/> in which the timeout occurred.
    /// </param>
    /// <returns>The same <see cref="CommandScopeBuilder{TResponse}"/> instance for chaining.</returns>
    public CommandScopeBuilder<TResponse> OnTimeout(Action<Exception, MeshContext> handler)
    {
        _onTimeout = handler;
        return this;
    }

    /// <summary>
    /// Adds a cancellation token that can abort the command execution.
    /// </summary>
    /// <param name="ct">The cancellation token to observe.</param>
    /// <returns>The same <see cref="CommandScopeBuilder{TResponse}"/> instance for chaining.</returns>
    public CommandScopeBuilder<TResponse> WithCancellation(CancellationToken ct)
    {
        _ct = ct;
        return this;
    }

    /// <summary>
    /// Streams responses asynchronously from the command execution.
    /// </summary>
    /// <returns>An async stream of <typeparamref name="TResponse"/> items.</returns>
    public IAsyncEnumerable<TResponse> StreamAsync() =>
        _scope.StreamAsync(_command, _timeout, _onTimeout, _ct);

    /// <summary>
    /// Streams results asynchronously from the command execution,
    /// including metadata such as success/failure state.
    /// </summary>
    /// <returns>An async stream of <see cref="Result{T}"/> items containing responses and status.</returns>
    public IAsyncEnumerable<Result<TResponse>> StreamResultAsync() =>
        _scope.StreamResultAsync(_command, _timeout, _ct);

    /// <summary>
    /// Sends the command asynchronously without expecting a response stream.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the send operation.</returns>
    public Task SendAsync() =>
        _scope.SendAsync(_command, _timeout, _onTimeout, _ct);
}
