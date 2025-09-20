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
    /// Registers a pending request that is awaiting a reply.
    /// The associated TaskCompletionSource will be completed when the reply arrives.
    /// </summary>
    /// <param name="pendingReply">An object containing the correlation ID and the TaskCompletionSource to complete upon reply.</param>
    public void RegisterPending(PendingReply<byte[]> pendingReply) =>
        _pending.TryAdd(pendingReply.CorrelationId, pendingReply);

    /// <summary>
    /// Attempts to remove a pending request. This is typically used when a request
    /// is cancelled or times out, to prevent a late reply from being processed.
    /// </summary>
    /// <param name="corrId">The correlation ID of the pending request to remove.</param>
    /// <returns>True if the pending request was found and removed; otherwise, false.</returns>
    public bool TryUnregister(ulong corrId)
    {
        // Attempts to remove the pending request from the dictionary.
        // This is the correct implementation, returning true on successful removal.
        return _pending.TryRemove(corrId, out var _);
    }

    /// <summary>
    /// Handles incoming reply messages from a NetMQ Socket.
    /// It extracts the correlation ID, finds the corresponding pending request,
    /// and completes the task with the received payload.
    /// </summary>
    /// <remarks>
    /// The expected NetMQMessage format is a multi-part message where:
    /// - Frame 1 contains the correlation ID (ulong).
    /// - Frame 2 contains the response payload (byte[]).
    /// Other frames (like the router identity in Frame 0) are ignored.
    /// </remarks>
    /// <param name="sender">The Socket object that raised the event.</param>
    /// <param name="e">The event arguments containing the Socket and state.</param>
    public void ReceivedFromRouter(object sender, NetMQSocketEventArgs e)
    {
        var msg = new NetMQMessage();

        // Continuously process all available multipart messages from the Socket.
        // Expecting a specific number of frames for a valid reply message.
        while (e.Socket.TryReceiveMultipartMessage(ref msg, 4))
        {
            // The correlation ID is expected in the second frame (index 1).
            ulong corrId = MemoryMarshal.Read<ulong>(msg[1].Buffer);

            // Attempt to find and remove the pending request associated with the correlation ID.
            if (_pending.TryRemove(corrId, out var pending))
            {
                // To avoid a race condition, check if the task has already been completed
                // (e.g., by a timeout exception) before trying to set the result.
                if (!pending.IsCompleted)
                {
                    // The payload is expected in the third frame (index 2).
                    // Complete the task with the payload as the result.
                    pending.SetResult(msg[2].Buffer);
                }
            }
        }
    }
}