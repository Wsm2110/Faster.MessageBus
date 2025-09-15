using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using NetMQ;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Routes command replies back to their originating request using correlation IDs.
/// Maintains a pending dictionary of TaskCompletionSource objects for awaiting tasks.
/// </summary>
public class CommandReplyHandler : ICommandReplyHandler
{
    // Holds the pending requests keyed by correlation ID.
    // When a reply is received, the corresponding TaskCompletionSource is completed.
    private readonly ConcurrentDictionary<ulong, PendingReply<byte[]>> _pending =
        new ConcurrentDictionary<ulong, PendingReply<byte[]>>();

    /// <summary>
    /// Registers a pending request by correlation ID.
    /// The TaskCompletionSource will be completed when the reply arrives.
    /// </summary>
    /// <param name="correlationID">Unique ID associated with this request.</param>
    /// <param name="pendingReply">TaskCompletionSource to complete upon reply.</param>
    public void RegisterPending(PendingReply<byte[]> pendingReply) =>
        _pending.TryAdd(pendingReply.CorrelationId, pendingReply);

    /// <summary>
    /// Attempts to remove a pending request without completing it.
    /// Useful for cancellation or cleanup.
    /// </summary>
    /// <param name="corrId">Correlation ID of the pending request.</param>
    /// <returns>True if the pending request was found and removed; otherwise false.</returns>
    public bool TryUnregister(ulong corrId)
    {
        if (_pending.TryRemove(corrId, out var _))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles incoming replies from DealerSockets.
    /// Matches messages based on correlation ID and completes the original Task.
    /// Expected message format: [identity][empty][correlationId][payload]
    /// </summary>
    /// <param name="sender">The socket raising the event.</param>
    /// <param name="e">Event arguments containing the received message.</param>
    public void ReceivedFromRouter(object sender, NetMQSocketEventArgs e)
    {
        var msg = new NetMQMessage();

        while (e.Socket.TryReceiveMultipartMessage(ref msg, 4))
        {
            ulong corrId = MemoryMarshal.Read<ulong>(msg[1].Buffer);

            if (_pending.TryRemove(corrId, out var pending))
            {
                pending.SetResult(msg[2].Buffer);
            }
        }
    }
}
