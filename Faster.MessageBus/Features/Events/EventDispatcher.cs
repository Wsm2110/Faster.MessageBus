using CommunityToolkit.HighPerformance.Buffers;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Features.Events.Shared;
using Faster.MessageBus.Shared;
using System.Runtime.InteropServices;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// A dispatcher responsible for serializing and publishing events to the message bus.
/// </summary>
/// <param name="scheduler">The scheduler responsible for queuing the event for transmission on a dedicated thread.</param>
/// <param name="serializer">The serializer used to convert event objects into a binary format.</param>
/// <param name="socketManager">The manager for the underlying network sockets used for publishing.</param>
public class EventDispatcher(
    IEventScheduler scheduler,
    IEventSerializer serializer,
    IEventSocketManager socketManager) : IEventDispatcher
{
    /// <summary>
    /// Serializes and publishes an event to the message bus. The operation is offloaded to a scheduler for non-blocking execution.
    /// </summary>
    /// <param name="event">The event to publish.</param>
    public void Publish(IEvent @event)
    {
        using var writer = new ArrayPoolBufferWriter<byte>();
        serializer.Serialize(@event, writer);
        var topic = @event.GetType().Name;
        scheduler.Invoke(new ScheduleEvent(socketManager.PublisherSocket, topic, writer.WrittenMemory));
    }
}