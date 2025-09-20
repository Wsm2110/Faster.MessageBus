using CommunityToolkit.HighPerformance.Buffers;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Features.Events.Shared;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Responsible for serializing and publishing events to the message bus.
/// Handles event scheduling, serialization, and socket management for outbound events.
/// </summary>
public class EventDispatcher : IEventDispatcher
{
    private readonly IEventScheduler _scheduler;
    private readonly IEventSerializer _serializer;
    private readonly IEventSocketManager _socketManager;

    /// <summary>
    /// Initializes a new instance of <see cref="EventDispatcher"/>.
    /// Ensures the startup sequence is executed before event dispatching begins.
    /// </summary>
    /// <param name="provider">The DI service provider for resolving dependencies.</param>
    /// <param name="scheduler">Queues events for transmission on a dedicated thread.</param>
    /// <param name="serializer">Serializes event objects into binary format.</param>
    /// <param name="socketManager">Manages network sockets for publishing events.</param>
    public EventDispatcher(
        IServiceProvider provider,
        IEventScheduler scheduler,
        IEventSerializer serializer,
        IEventSocketManager socketManager)
    {
        // Store dependencies for use in Publish
        _scheduler = scheduler;
        _serializer = serializer;
        _socketManager = socketManager;     
    }

    /// <summary>
    /// Serializes and publishes an event to the message bus.
    /// The operation is offloaded to a scheduler for non-blocking execution.
    /// </summary>
    /// <param name="event">The event to publish.</param>
    public void Publish(IEvent @event)
    {
        // Serialize the event to a buffer
        var writer = new ArrayPoolBufferWriter<byte>();
        _serializer.Serialize(@event, writer);

        // Use the event type name as the topic
        var topic = @event.GetType().Name;

        // Schedule the event for transmission using the socket manager
        _scheduler.Invoke(new ScheduleEvent(_socketManager.PublisherSocket, topic, writer));
    }
}