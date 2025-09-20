using Faster.MessageBus.Contracts;
using System.Runtime.CompilerServices;

namespace Faster.MessageBus.Features.Commands.Contracts;

/// <summary>
/// Defines the contract for sending commands to one or more endpoints within the same machine,
/// but outside of the current process (Inter-Process Communication).
/// </summary>
public interface ICommandScope
{
    /// <summary>
    /// Sends a command to all listening endpoints on the local machine and returns a stream of their responses.
    /// This method is designed for "scatter-gather" scenarios where a single request can yield multiple replies.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response objects expected in the stream.</typeparam>
    /// <param name="topic">The unique identifier for the command, used for routing.</param>
    /// <param name="command">The command object containing the data to be sent.</param>
    /// <param name="timeout">The maximum total time to wait for all responses to arrive.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the entire operation. This token is automatically passed by <c>await foreach</c> loops.</param>
    /// <returns>
    /// An asynchronous stream (<see cref="IAsyncEnumerable{T}"/>) that yields each response of type <typeparamref name="TResponse"/> as it is received from an endpoint on the machine.
    /// </returns>
    /// <remarks>
    /// You can consume the stream of responses using an <c>await foreach</c> loop.
    /// <code>
    /// await foreach (var response in commandScope.SendAsync(topic, command, timeout))
    /// {
    ///     // Process each response as it arrives
    /// }
    /// </code>
    /// </remarks>
    IAsyncEnumerable<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Sends a command to all listening endpoints on the local machine and awaits their completion, without returning any data.
    /// This is suitable for notification-style or "fire-and-await-completion" commands.
    /// </summary>
    /// <remarks>The method name "SendAsync" is unconventional; standard C# naming would be "SendAsync".</remarks>
    /// <param name="topic">The unique identifier for the command, used for routing.</param>
    /// <param name="command">The command object containing the data to be sent.</param>
    /// <param name="timeout">The maximum time to wait for completion acknowledgments.</param>
    /// <param name="ct">An optional cancellation token to cancel the operation externally.</param>
    /// <returns>A <see cref="Task"/> that completes when all endpoints have acknowledged the command or the operation times out.</returns>
    Task SendAsync(ICommand command, TimeSpan timeout, CancellationToken ct = default);
}