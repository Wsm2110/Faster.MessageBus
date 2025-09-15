using Faster.MessageBus.Contracts;

namespace Faster.MessageBus.Features.Commands.Contracts;

/// <summary>
/// Defines the contract for sending commands to handlers within the same process.
/// </summary>
/// <remarks>
/// This scope represents the most direct and highest-performance communication channel, as it typically
/// involves in-memory operations and avoids network or inter-process communication overhead.
/// A command sent via this scope is handled by a single, definitive handler within the running application.
/// </remarks>
public interface ILocalCommandScope
{
    /// <summary>
    /// Asynchronously sends a command to its registered handler within the current process and awaits a single response.
    /// </summary>
    /// <typeparam name="TResponse">The expected type of the response object.</typeparam>
    /// <param name="topic">The unique identifier for the command, used for routing to the correct handler.</param>
    /// <param name="command">The command object containing the data to be sent.</param>
    /// <param name="timeout">The maximum time to wait for a response.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation externally.</param>
    /// <returns>A <see cref="Task{TResult}"/> that resolves to the response object of type <typeparamref name="TResponse"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown if the operation times out or is canceled via the <paramref name="ct"/> token.</exception>
    Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Asynchronously sends a command to its handler within the current process and awaits completion, without returning any data.
    /// This is suitable for notification-style or "fire-and-await-completion" commands.
    /// </summary>
    /// <remarks>The method name "SendASync" is unconventional; standard C# naming would be "SendAsync".</remarks>
    /// <param name="topic">The unique identifier for the command, used for routing.</param>
    /// <param name="command">The command object containing the data to be sent.</param>
    /// <param name="timeout">The maximum time to wait for a completion acknowledgment.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation externally.</param>
    /// <returns>A <see cref="Task"/> that completes when the command has been handled.</returns>
    Task SendASync(ICommand command, TimeSpan timeout, CancellationToken ct = default);
}