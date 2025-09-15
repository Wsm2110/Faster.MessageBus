using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Concrete implementation of <see cref="IEventHandlerProvider"/> using dependency injection.
/// Its single responsibility is to build and store _handler actions.
/// (Formerly NoticationHandlerProvider)
/// </summary>
internal class EventHandlerProvider : IEventHandlerProvider
{
    private readonly Dictionary<string, Action<byte[]>> _eventHandlers = new();
    private readonly IServiceProvider _provider;
    private readonly IEventSerializer _serializer;
    private readonly IEventHandlerAssemblyScanner _scanner;

    /// <inheritdoc />
    public IEnumerable<string> GetRegisteredTopics() => _eventHandlers.Keys;

    public EventHandlerProvider(IServiceProvider provider,
        IEventSerializer serializer,
        IEventHandlerAssemblyScanner scanner)
    {
        _provider = provider;
        _serializer = serializer;
        _scanner = scanner;     
        RegisterAll(scanner);
    }

    /// <inheritdoc />
    public void AddEventHandler<TEvent>(string topic) where TEvent : IEvent
    {
        _eventHandlers[topic] = payload =>
        {
            // Resolve the appropriate consumer from the service _provider
            if (_provider.GetService(typeof(IEventHandler<TEvent>)) is not IEventHandler<TEvent> handler)
            {
                throw new InvalidOperationException($"No _handler registered in the DI container for {typeof(IEventHandler<TEvent>)}.");
            }

            // Deserialize the message payload
            var message = _serializer.Deserialize<TEvent>(payload);

            // Invoke the consumer's Handle method
            handler.Handle(message);
        };
    }

    /// <inheritdoc />
    public Action<byte[]> GetHandler(string topic)
    {
        if (_eventHandlers.TryGetValue(topic, out var handler))
        {
            return handler;
        }

        throw new KeyNotFoundException($"No consumer _handler registered for topic '{topic}'.");
    }

    /// <summary>
    /// Finds all consumers using the provided finder and registers them with the registry.
    /// By convention, the message type's name is used as the topic.
    /// </summary>
    public void RegisterAll(IEventHandlerAssemblyScanner scanner)
    {
        var consumerTypes = scanner.FindEventTypes();

        // Use reflection to call the generic AddEventHandler<TEvent> method
        var addMethod = typeof(IEventHandlerProvider)
            .GetMethod(nameof(IEventHandlerProvider.AddEventHandler))!;

        foreach (var (_, messageType) in consumerTypes)
        {
            var genericMethod = addMethod.MakeGenericMethod(messageType);

            // By convention, use the message type's name as the topic.
            // This could be made more flexible, e.g., by using a custom attribute on the message class.
            genericMethod.Invoke(this, new object[] { messageType.Name });
        }
    }
}
