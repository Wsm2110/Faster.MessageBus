using Faster.MessageBus.Features.Commands.Shared;
using NetMQ;

namespace Faster.MessageBus.Features.Commands.Contracts;

/// <summary>
/// Defines a contract for handling and correlating asynchronous responses from a NetMQ Router socket.
/// </summary>
/// <remarks>
/// This interface is central to a request-reply pattern. The typical workflow is:
/// 1. A client creates and registers a <see cref="PendingReply{TResult}"/> object using <see cref="RegisterPending"/>.
/// 2. The client sends a request message containing the <see cref="PendingReply{TResult}.CorrelationId"/>.
/// 3. The handler listens for incoming messages on a Router socket via the <see cref="ReceivedFromRouter"/> method.
/// 4. When a response arrives, the handler uses the message's correlation ID to find the original pending reply.
/// 5. The handler completes the pending reply with the response data, which in turn completes the ValueTask awaited by the client.
/// 6. The <see cref="TryUnregister"/> method is used to clean up pending replies in cases like timeouts or cancellations.
/// </remarks>
public interface ICommandReplyHandler
{
    /// <summary>
    /// Handles the event raised when a message is received from a NetMQ socket, expected to be a RouterSocket.
    /// </summary>
    /// <remarks>
    /// The implementation of this method is responsible for parsing the incoming message frames to
    /// extract the correlation ID and payload, find the corresponding pending request, and complete it.
    /// </remarks>
    /// <param name="sender">The NetMQ socket object that fired the event.</param>
    /// <param name="e">The event arguments containing the received message via <c>e.Socket</c>.</param>
    void ReceivedFromRouter(object sender, NetMQSocketEventArgs e);

    /// <summary>
    /// Registers a pending reply operation that is awaiting a response.
    /// This should be called before the corresponding request message is sent.
    /// </summary>
    /// <param name="pending">
    /// The <see cref="PendingReply{TResult}"/> object representing the asynchronous operation.
    /// It contains the correlation ID used to match the response.
    /// </param>
    void RegisterPending(PendingReply<byte[]> pending);

    /// <summary>
    /// Attempts to unregister and remove a pending reply operation from tracking.
    /// </summary>
    /// <remarks>
    /// This is typically used to handle client-side timeouts or cancellations, preventing
    /// resource leaks from replies that may never arrive.
    /// </remarks>
    /// <param name="corrId">The correlation ID of the pending reply to remove.</param>
    /// <returns>
    /// <c>true</c> if a pending reply with the specified correlation ID was found and removed;
    /// otherwise, <c>false</c>.
    /// </returns>
    bool TryUnregister(ulong corrId);
}