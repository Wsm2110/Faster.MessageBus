namespace Faster.MessageBus.Contracts // Namespace for the message bus contracts.
{
    /// <summary>
    /// Defines the contract for an Event Aggregator, responsible for managing
    /// communication between loosely coupled components via events.
    /// </summary>
    /// <remarks>
    /// An Event Aggregator acts as a centralized messaging hub, allowing publishers
    /// and subscribers to communicate without needing direct references to one another.
    /// It inherits from <see cref="IDisposable"/> to ensure proper cleanup of resources,
    /// such as internal subscription dictionaries or thread-safe collections.
    /// </remarks>
    public interface IEventAggregator : IDisposable // An interface defining a central message hub that must be disposable.
    {
        /// <summary>
        /// Publishes an event of a specified type to all registered subscribers.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event being published (must be a class/struct).</typeparam>
        /// <param name="e">The event instance to publish.</param>
        void Publish<TEvent>(TEvent e); // Method to send an event to all interested subscribers.

        /// <summary>
        /// Subscribes a handler method to a specific event type.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event to subscribe to.</typeparam>
        /// <param name="handler">The action (method) that will be executed when the event is published.</param>
        void Subscribe<TEvent>(Action<TEvent> handler); // Method to register a handler for a specific event type.

        /// <summary>
        /// Unsubscribes a previously registered handler method from a specific event type.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event to unsubscribe from.</typeparam>
        /// <param name="handler">The action (method) to remove from the subscription list.</param>
        void Unsubscribe<TEvent>(Action<TEvent> handler); // Method to remove a handler from the subscription list.
    }
}