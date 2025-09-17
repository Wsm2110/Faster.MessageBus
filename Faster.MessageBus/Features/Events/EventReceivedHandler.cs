using Faster.MessageBus.Features.Commands.Extensions;
using Faster.MessageBus.Features.Events.Contracts;
using NetMQ;
using System.Buffers;

namespace Faster.MessageBus.Features.Events;

internal class EventReceivedHandler(IEventHandlerProvider eventHandlerProvider ,IServiceProvider serviceProvider, IEventSerializer serializer) : IEventReceivedHandler
{
    public void OnEventReceived(object? sender, NetMQSocketEventArgs e)
    {
        var msg = new NetMQMessage();
        while (e.Socket.TryReceiveMultipartMessage(ref msg))
        {

            // Offload processing to an async method to avoid blocking the poller thread.
            // The fire-and-forget pattern is used here for maximum throughput.
            _ = HandleRequestAsync(msg);
        }
    }

    /// <summary>
    /// Asynchronously processes a single request message.
    /// </summary>
    /// <remarks>
    /// This method parses the request, dispatches it to the business logic handler,
    /// builds the response message, and queues it for sending on the poller thread.
    /// </remarks>
    /// <param name="msg">The incoming NetMQ message to process.</param>
    private async Task HandleRequestAsync(NetMQMessage msg)
    {
        // Parse the incoming message frames without copying where possible.
        var identity = msg[0];
        var topic = FastConvert.BytesToUlong(msg[1].Buffer);
        var payloadFrame = msg[3];

        // Dispatch the payload to the appropriate command handler.
        var payload = new ReadOnlySequence<byte>(payloadFrame.Buffer, 0, payloadFrame.MessageSize);

        //await eventHandlerProvider.GetHandler(topic).Invoke(serviceProvider, serializer, payload);
    }
}

