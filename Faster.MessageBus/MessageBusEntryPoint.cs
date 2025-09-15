using Faster.MessageBus;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Events;
using Faster.MessageBus.Features.Events.Contracts;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The main, unified entry point for the entire message bus system.
/// </summary>
/// <remarks>
/// This class serves two primary purposes:
/// 1. As a façade, it provides a single, injectable access point to the core functionalities of the message bus:
///    the <see cref="EventDispatcher"/> for publish-subscribe messaging and the <see cref="CommandDispatcher"/> for request-reply messaging.
/// 2. As an <see cref="IServiceInstaller"/>, it handles its own registration with the dependency injection container.
/// <example>
/// Usage in a service:
/// <code>
/// public class MyService
/// {
///     private readonly IMessageBusEntryPoint _messageBus;
///
///     public MyService(IMessageBusEntryPoint messageBus)
///     {
///         _messageBus = messageBus;
///     }
///
///     public async Task DoSomething()
///     {
///         // Publish an event
///         _messageBus.EventDispatcher.Publish(new MyEvent());
///
///         // Send a command and get a reply
///         var response = await _messageBus.CommandDispatcher.Local.Send(topic, command, timeout);
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public class MessageBusEntryPoint : IServiceInstaller, IMessageBusEntryPoint
{
    /// <summary>
    /// Gets the dispatcher for publishing events using the publish-subscribe pattern.
    /// </summary>
    /// <value>The <see cref="IEventDispatcher"/> implementation.</value>
    public IEventDispatcher EventDispatcher { get; }

    /// <summary>
    /// Gets the dispatcher for sending commands using the request-reply pattern across various scopes.
    /// </summary>
    /// <value>The <see cref="ICommandDispatcher"/> implementation.</value>
    public ICommandDispatcher CommandDispatcher { get; }

    /// <summary>
    /// 
    /// </summary>
    public MessageBusEntryPoint()
    {
        // Used for reflection...
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageBusEntryPoint"/> class.
    /// </summary>
    /// <param name="eventDispatcher">The concrete event dispatcher implementation.</param>
    /// <param name="commandDispatcher">The concrete command dispatcher implementation.</param>
    public MessageBusEntryPoint(IEventDispatcher eventDispatcher,
        ICommandDispatcher commandDispatcher, IServiceProvider serviceProvider)
    {
        EventDispatcher = eventDispatcher;
        CommandDispatcher = commandDispatcher;

        // initialize startup classes
        serviceProvider.GetService<IStartup>();
    }

    /// <inheritdoc/>
    public void Install(IServiceCollection serviceCollection)
    {
        // Registers this entry point as a singleton, making IMessageBusEntryPoint
        // available for injection throughout the application.
        serviceCollection.AddSingleton<IMessageBusEntryPoint, MessageBusEntryPoint>();
        serviceCollection.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        serviceCollection.AddSingleton<IEventDispatcher, EventDispatcher>();

        serviceCollection.AddSingleton<IStartup, Startup>();

    }
}