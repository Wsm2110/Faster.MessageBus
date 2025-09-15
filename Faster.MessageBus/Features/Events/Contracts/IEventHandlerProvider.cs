using Faster.MessageBus.Contracts;

namespace Faster.MessageBus.Features.Events.Contracts;

/// <summary>
/// Defines methods for registering and retrieving message consumers by topic.
/// (Formerly IEventHandlerProvider)
/// </summary>
public interface IEventHandlerProvider
{
    /// <summary>
    /// Registers a consumer for the given topic that handles messages of type <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The type of message the consumer handles.</typeparam>
    /// <param name="topic">The topic associated with this message type.</param>
    void AddEventHandler<TEvent>(string topic) where TEvent : IEvent;

    /// <summary>
    /// Gets the byte array _handler associated with a specific topic.
    /// </summary>
    /// <param name="topic">The topic for which to retrieve the _handler.</param>
    /// <returns>An action that takes a byte array as input (the serialized message).</returns>
    Action<byte[]> GetHandler(string topic);

    /// <summary>
    /// Returns the list of currently registered consumer topic names.
    /// </summary>
    /// <returns>A collection of topic strings.</returns>
    IEnumerable<string> GetRegisteredTopics();
}


