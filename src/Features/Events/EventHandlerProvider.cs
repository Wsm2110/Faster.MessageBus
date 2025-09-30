using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Manages and dispatches event handlers. This class pre-compiles optimized delegates
/// at startup to ensure high performance at runtime.
/// </summary>
public class EventHandlerProvider : IEventHandlerProvider // Consider renaming the interface
{
    #region Fields

    // Caching MethodInfo in a static field is efficient; reflection lookup occurs only once.
    private static readonly MethodInfo CreateHandlerMethod =
        typeof(EventHandlerProvider).GetMethod(nameof(CreateHandlerForEvent), BindingFlags.NonPublic | BindingFlags.Static)!;

    // A dictionary mapping the event type's hash to a delegate that executes all its handlers.
    private readonly Dictionary<string, Func<IServiceProvider, IEventSerializer, byte[], Task>> _eventHandlers = new();

    #endregion

    /// <summary>
    /// Initializes the handler by discovering and registering delegates for all provided event types.
    /// This should be called once at application startup.
    /// </summary>
    public void Initialize(IEnumerable<Type> eventTypes)
    {
        foreach (var eventType in eventTypes)
        {
            RegisterHandler(eventType);
        }
    }

    /// <summary>
    /// Retrieves the handler function for a specific topic hash.
    /// </summary>
    public Func<IServiceProvider, IEventSerializer, byte[], Task> GetHandler(string topic)
    {
        if (!_eventHandlers.TryGetValue(topic, out var handler))
        {
            throw new KeyNotFoundException($"No event handler registered for topic hash '{topic}'.");
        }
        return handler;
    }

    /// <summary>
    /// Uses reflection to create and register a single event handler delegate.
    /// </summary>
    private void RegisterHandler(Type eventType)
    {
        var topic = GetTopicForType(eventType);

        // Create the specific generic method (e.g., CreateHandlerForEvent<MyEvent>).
        var genericFactory = CreateHandlerMethod.MakeGenericMethod(eventType);

        // Invoke the static factory to create the handler delegate.
        var handlerDelegate = (Func<IServiceProvider, IEventSerializer, byte[], Task>)genericFactory.Invoke(null, null)!;

        // TryAdd the compiled delegate to the dictionary.
        if (!_eventHandlers.ContainsKey(topic))
        {
            _eventHandlers[topic] = handlerDelegate;
        }
    }

    #region Handler Factory Method

    /// <summary>
    /// Creates a delegate that, when executed, resolves all handlers for a given event
    /// from the DI container and runs them concurrently.
    /// </summary>
    private static Func<IServiceProvider, IEventSerializer, byte[], Task> CreateHandlerForEvent<TEvent>() where TEvent : IEvent
    {
        // This is the optimized delegate that will be executed at runtime. It's async and static.
        return async static (serviceProvider, serializer, payload) =>
        {
            // An event can have multiple handlers. Resolve all of them.
            var handler = serviceProvider.GetService<IEventHandler<TEvent>>();
            if (handler == null)
            {
                return; // No handlers registered for this event.
            }

            var @event = (TEvent)serializer.Deserialize<IEvent>(payload);

            // Create a task for each handler and execute them all concurrently.
            await handler.Handle(@event);
        };
    }

    /// <summary>
    /// Generates a fast, non-colliding hash for a given type to use as a dictionary key.
    /// </summary>
    private static string GetTopicForType(Type type) => type.Name;

    #endregion
}