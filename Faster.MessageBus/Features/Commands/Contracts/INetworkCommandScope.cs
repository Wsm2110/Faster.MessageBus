using Faster.MessageBus.Contracts;

namespace Faster.MessageBus.Features.Commands.Contracts
{
    /// <summary>
    /// Defines the contract for sending commands to endpoints across a wide area network (WAN).
    /// </summary>
    /// <remarks>
    /// This scope is the broadest, targeting services that may be running in different data centers or geographic locations.
    /// Implementations are expected to handle the complexities of remote communication, such as service discovery,
    /// increased latency, and potential network failures.
    /// </remarks>
    public interface INetworkCommandScope
    {
        /// <summary>
        /// Broadcasts a command across the network to all relevant endpoints and returns an asynchronous stream of their responses.
        /// This method is designed for "scatter-gather" scenarios across a distributed system.
        /// </summary>
        /// <typeparam name="TResponse">The type of the response objects expected in the stream.</typeparam>
        /// <param name="topic">The unique identifier for the command, used for routing to the correct remote services.</param>
        /// <param name="command">The command object containing the data to be sent.</param>
        /// <param name="timeout">The maximum total time to wait for all responses to arrive from the network.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the entire operation.</param>
        /// <returns>
        /// An asynchronous stream (<see cref="IAsyncEnumerable{T}"/>) that yields each response of type <typeparamref name="TResponse"/> as it is received from a remote endpoint.
        /// </returns>
        /// <remarks>
        /// Due to network latency, responses may arrive at different times. The stream can be consumed using an <c>await foreach</c> loop.
        /// <code>
        /// await foreach (var response in commandScope.SendAsync(topic, command, timeout))
        /// {
        ///     // Process each response from a remote service
        /// }
        /// </code>
        /// </remarks>
        IAsyncEnumerable<TResponse> Send<TResponse>(ulong topic, ICommand<TResponse> command, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// Broadcasts a command across the network and awaits completion acknowledgments from remote endpoints.
        /// This is suitable for notification-style or "fire-and-await-completion" commands where no data is returned.
        /// </summary>
        /// <remarks>The method name "SendASync" is unconventional; standard C# naming would be "SendAsync".</remarks>
        /// <param name="topic">The unique identifier for the command, used for routing.</param>
        /// <param name="command">The command object containing the data to be sent.</param>
        /// <param name="timeout">The maximum time to wait for completion acknowledgments from all remote endpoints.</param>
        /// <param name="ct">An optional cancellation token to cancel the operation externally.</param>
        /// <returns>A <see cref="Task"/> that completes when all remote endpoints have acknowledged the command or the operation times out.</returns>
        Task SendASync(ulong topic, ICommand command, TimeSpan timeout, CancellationToken ct = default);
    }
}