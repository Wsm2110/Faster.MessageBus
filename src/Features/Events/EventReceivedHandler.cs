using Faster.MessageBus.Features.Events.Contracts;
using NetMQ;
using System.Text;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Handles incoming event messages from a NetMQ socket.
/// It acts as the entry point for network events, receiving raw messages,
/// parsing them, and dispatching them to the appropriate application logic for processing.
/// </summary>
internal class EventReceivedHandler(IEventHandlerProvider eventHandlerProvider, IServiceProvider serviceProvider, IEventSerializer serializer) : IEventReceivedHandler
{
    /// <summary>
    /// This is the callback method executed by the NetMQ poller when a message is available on the socket.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The socket event arguments, which include the socket that has the message.</param>
    public void OnEventReceived(object? sender, NetMQSocketEventArgs e)
    {
        // Initialize a new message object to receive the data.
        var msg = new NetMQMessage();

        // The while loop efficiently drains the socket of all pending messages in a single batch,
        // which is crucial for high-throughput scenarios.
        while (e.Socket.TryReceiveMultipartMessage(ref msg))
        {
            // Offload the actual processing to an async "fire-and-forget" task.
            // This immediately frees up the poller thread to continue receiving more messages,
            // preventing it from becoming a bottleneck.
            _ = HandleRequestAsync(msg);
        }
    }

    /// <summary>
    /// Asynchronously parses and processes a single incoming multipart message.
    /// This method extracts the topic and payload, then invokes the correct handler
    /// to perform the business logic.
    /// </summary>
    /// <param name="msg">The incoming NetMQ multipart message to process.</param>
    private Task HandleRequestAsync(NetMQMessage msg)
    {
        // By convention, the first frame (msg[0]) of the message is the topic string.
        // It's decoded from UTF-8 bytes back into a string.
        var topic = Encoding.UTF8.GetString(msg[0].Buffer);

        // By convention, the second frame (msg[1]) is the raw binary payload.
        // We pass the underlying byte buffer directly to the handler to avoid unnecessary memory copies.
        var payload = msg[1].Buffer;

        // Retrieve the appropriate handler for the given topic and invoke it with all necessary dependencies.
        eventHandlerProvider.GetHandler(topic).Invoke(serviceProvider, serializer, payload);

        // Return a completed task as this is a "fire-and-forget" operation.
        return Task.CompletedTask;
    }
}