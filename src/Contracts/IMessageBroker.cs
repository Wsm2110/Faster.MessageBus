using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Defines the main, unified contract for interacting with the entire message bus system.
/// </summary>
/// <remarks>
/// This interface serves as a high-level façade, providing a single, injectable access point to the core
/// functionalities of the message bus: the <see cref="CommandDispatcher"/> for request-reply messaging
/// and the <see cref="EventDispatcher"/> for publish-subscribe messaging.
/// <example>
/// Usage in a service:
/// <code>
/// public class MyService
/// {
///     private readonly IMessageBroker _messageBus;
///
///     public MyService(IMessageBroker messageBus)
///     {
///         _messageBus = messageBus;
///     }
///
///     public async Task DoWork()
///     {
///         // Publish an event via the event dispatcher
///         _messageBus.EventDispatcher.Publish(new MyEvent());
///
///         // SendAsync a command via the command dispatcher
///         var response = await _messageBus.CommandDispatcher.Local.SendAsync(topic, command, timeout);
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public interface IMessageBroker
{
    /// <summary>
    /// Gets the dispatcher for sending commands using the request-reply pattern across various scopes.
    /// </summary>
    /// <value>The <see cref="ICommandDispatcher"/> implementation.</value>
    ICommandDispatcher CommandDispatcher { get; }

    /// <summary>
    /// Gets the dispatcher for publishing events using the publish-subscribe pattern.
    /// </summary>
    /// <value>The <see cref="IEventDispatcher"/> implementation.</value>
    IEventDispatcher EventDispatcher { get; }

    // Note: It's somewhat unconventional for a primary API contract like this to also be an IServiceInstaller.
    // Usually, the installation logic is kept separate. This documentation reflects the interface as provided.

    /// <summary>
    /// Registers the necessary services for the message bus with the dependency injection container.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add service registrations to.</param>
    void Install(IServiceCollection serviceCollection);
}