using Faster.MessageBus.Contracts;

namespace Faster.MessageBus.Features.Events.Contracts
{
    /// <summary>
    /// Defines a contract for dispatching events to be published on the message bus.
    /// This acts as the entry point for application code to send an event.
    /// </summary>
    public interface IEventDispatcher
    {
        /// <summary>
        /// Publishes an event to the message bus.
        /// Implementations typically handle serialization and queuing the event for transmission on a background thread.
        /// </summary>
        /// <param name="event">The event object to publish. The '@' symbol is used because 'event' is a C# keyword.</param>
        void Publish(IEvent @event);
    }
}