using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Discovers, builds, and provides event handler delegates for different event types.
/// This class acts as a central registry, mapping event topics to their corresponding processing logic.
/// It uses reflection to dynamically find all event types at startup.
/// </summary>
internal class EventHandlerProvider : IEventHandlerProvider
{
    /// <summary>
    /// A dictionary that stores the compiled handler actions.
    /// The key is the event topic (typically the event type's name), and the value is the action
    /// that deserializes the payload and invokes the appropriate handler.
    /// </summary>
    private readonly Dictionary<string, Action<IServiceProvider, IEventSerializer, byte[]>> _eventHandlers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="EventHandlerProvider"/> class.
    /// It scans for all types implementing <see cref="IEvent"/> and pre-builds a handler for each.
    /// </summary>
    /// <param name="provider">The dependency injection service provider, used to resolve event handlers at runtime.</param>
    /// <param name="serializer">The serializer instance for deserializing event payloads.</param>
    /// <param name="scanner">The service responsible for finding all event types in the relevant assemblies.</param>
    public EventHandlerProvider(IServiceProvider provider,
        IEventSerializer serializer,
        IEventHandlerAssemblyScanner scanner)
    {
        // Discover all classes that implement the IEvent interface.
        var eventTypes = scanner.FindAllEvents();

        // Get a reference to the AddEventHandler<T> method for later generic invocation.
        var addMethod = typeof(EventHandlerProvider).GetMethod(nameof(AddEventHandler))!;

        // Iterate over each discovered event type to register a specific handler for it.
        foreach (var eventType in eventTypes)
        {
            // Create a generic version of the AddEventHandler method, e.g., AddEventHandler<MySpecificEvent>().
            var genericMethod = addMethod.MakeGenericMethod(eventType);

            // Invoke the generic method (e.g., AddEventHandler<MySpecificEvent>("MySpecificEvent")).
            // By convention, the topic is the name of the event class itself.
            genericMethod.Invoke(this, new object[]
            {
                eventType.Name
            });
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> GetRegisteredTopics() => _eventHandlers.Keys;

    /// <summary>
    /// Creates and registers a handler delegate for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event to handle, must implement <see cref="IEvent"/>.</typeparam>
    /// <param name="topic">The topic string used to identify the event type.</param>
    public void AddEventHandler<TEvent>(string topic) where TEvent : IEvent
    {
        // This delegate is the core logic. It's stored in the dictionary and executed when an event arrives.
        _eventHandlers[topic] = (serviceProvider, serializer, payload) =>
        {
            // Step 1: Resolve the specific event handler (e.g., IEventHandler<MySpecificEvent>) from the DI container.
            if (serviceProvider.GetService(typeof(IEventHandler<TEvent>)) is not IEventHandler<TEvent> handler)
            {
                // If no handler is registered in the DI container for this event type, we can't proceed.
                throw new InvalidOperationException($"No eventHandler registered in the DI container for {typeof(IEventHandler<TEvent>)}.");
            }

            // Step 2: Deserialize the raw byte payload into the specific event object.
            // The serializer must be configured to handle the IEvent base type (e.g., using Typeless mode).
            var message = (TEvent)serializer.Deserialize<IEvent>(payload);

            // Step 3: Invoke the handler's business logic with the deserialized event object.
            handler.Handle(message);
        };
    }

    /// <summary>
    /// Retrieves the handler action for a given topic.
    /// </summary>
    /// <param name="topic">The topic of the event to handle.</param>
    /// <returns>The executable action that will process the event.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if no handler is registered for the specified topic.</exception>
    public Action<IServiceProvider, IEventSerializer, byte[]> GetHandler(string topic)
    {
        if (_eventHandlers.TryGetValue(topic, out var handler))
        {
            return handler;
        }

        throw new KeyNotFoundException($"No eventHandler registered for topic '{topic}'.");
    }
}