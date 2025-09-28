using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Features.Events;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Features.Heartbeat.Contracts;
using Faster.MessageBus.Shared;
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
///     private readonly IMessageBroker _messageBus;
///
///     public MyService(IMessageBroker messageBus)
///     {
///         _messageBus = messageBus;
///     }
///
///     public async Task DoSomething()
///     {
///         // Publish an event
///         _messageBus.EventDispatcher.Publish(new MyEvent());
///
///         // StreamAsync a command and get a reply
///         var response = await _messageBus.CommandDispatcher.Local.StreamAsync(topic, command, timeout);
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public class MessageBroker : IMessageBroker
{
    /// <summary>
    /// Gets the dispatcher for publishing events using the publish-subscribe pattern.
    /// </summary>
    /// <value>The <see cref="IEventDispatcher"/> implementation.</value>
    public IEventDispatcher EventDispatcher { get; }

    /// <summary>
    /// Gets the dispatcher for sending commands using the request-reply pattern across various scopes.
    /// </summary>CommandDispatcher
    /// <value>The <see cref="ICommandDispatcher"/> implementation.</value>
    public ICommandDispatcher CommandDispatcher { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageBroker"/> class.
    /// </summary>
    /// <param name="eventDispatcher">The concrete event dispatcher implementation.</param>
    /// <param name="commandDispatcher">The concrete command dispatcher implementation.</param>
    public MessageBroker(IEventDispatcher eventDispatcher,
        ICommandDispatcher commandDispatcher,
        IServiceProvider serviceProvider, Mesh mesh)
    {
        EventDispatcher = eventDispatcher;
        CommandDispatcher = commandDispatcher;

        serviceProvider.GetRequiredService<CommandServer>();
        serviceProvider.GetRequiredService<IHeartBeatMonitor>();

        var scanner = serviceProvider.GetRequiredService<ICommandAssemblyScanner>();
        var handler = serviceProvider.GetRequiredService<ICommandMessageHandler>();

        var commandContext = scanner.ScanForCommands().ToList();
        handler.Initialize(commandContext);

        // Init bloom filter
        var filter = serviceProvider.GetRequiredService<ICommandRoutingFilter>();
        filter.Initialize(commandContext.Count);

        foreach (var command in commandContext)
        {
            var hash = WyHash.Hash(command.messageType.Name);
            filter.Add(hash);          
        }

        // start discovery once were registered 
        serviceProvider.GetRequiredService<IMeshDiscoveryService>()
            .Start(mesh.GetMeshInfo());
    }
}

public class ServiceInstaller : IServiceInstaller
{
    /// <inheritdoc/>
    public void Install(IServiceCollection serviceCollection)
    {
        // Registers this entry point as a singleton, making IMessageBroker
        // available for injection throughout the application.
        serviceCollection.AddSingleton<IMessageBroker, MessageBroker>();
        serviceCollection.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        serviceCollection.AddSingleton<IEventDispatcher, EventDispatcher>();
        serviceCollection.AddSingleton<IEventAggregator, EventAggregator>();
    }
}