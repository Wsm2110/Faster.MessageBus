using System.Buffers;

namespace Faster.MessageBus.Features.Events.Contracts;

/// <summary>
/// Defines a contract for a service that manages and provides access to
/// pre-compiled event handler delegates.
/// </summary>
public interface IEventHandlerProvider
{
    /// <summary>
    /// Initializes the handler by discovering and registering delegates for all provided event types.
    /// This should be called once at application startup.
    /// </summary>
    /// <param name="eventTypes">An enumerable of event types to register handlers for.</param>
    void Initialize(IEnumerable<Type> eventTypes);

    /// <summary>
    /// Retrieves the compiled handler function for a specific topic hash.
    /// </summary>
    /// <param name="topic">The ulong hash of the event type's full name.</param>
    /// <returns>A delegate that, when invoked, executes all registered handlers for the event.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown if no handler is registered for the specified topic.</exception>
    Func<IServiceProvider, IEventSerializer, byte[], Task> GetHandler(string topic);
}