using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Features.Events;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Features.Heartbeat.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Linq;

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
    /// </summary>
    /// <value>The <see cref="ICommandDispatcher"/> implementation.</value>
    public ICommandDispatcher CommandDispatcher { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageBroker"/> class,
    /// sets up command routing, bloom filters, and starts discovery.
    /// </summary>
    /// <param name="eventDispatcher">The concrete event dispatcher implementation.</param>
    /// <param name="commandDispatcher">The concrete command dispatcher implementation.</param>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="mesh">The mesh instance used for discovery initialization.</param>
    public MessageBroker(IEventDispatcher eventDispatcher,
        ICommandDispatcher commandDispatcher,
        IServiceProvider serviceProvider, 
        IEventAggregator eventAggregator,
        MeshApplication mesh)
    {
        EventDispatcher = eventDispatcher;
        CommandDispatcher = commandDispatcher;

        // Ensure required services are registered
        serviceProvider.GetRequiredService<CommandServer>();
        serviceProvider.GetRequiredService<IHeartBeatMonitor>();

        var scanner = serviceProvider.GetRequiredService<ICommandAssemblyScanner>();
        var handler = serviceProvider.GetRequiredService<ICommandMessageHandler>();

        // Scan for commands and initialize the handler
        var commandContext = scanner.ScanForCommands().ToList();
        handler.Initialize(commandContext);

        // Initialize bloom filter for command routing
        var filter = serviceProvider.GetRequiredService<ICommandRoutingFilter>();
        filter.Initialize(commandContext.Count);

        foreach (var command in commandContext)
        {
            var hash = WyHash.Hash(command.messageType.Name);
            filter.Add(hash);
        }

        var context = mesh.GetMeshContext(filter.GetMembershipTable());

        // Start discovery after registration
        serviceProvider.GetRequiredService<IMeshDiscoveryService>()
            .Start(context);

        context.Self = true;

        //register self to socketmanagers
        eventAggregator.Publish(new MeshJoined(context));
    }
}

/// <summary>
/// Handles dependency injection registration for the message bus.
/// </summary>
public class ServiceInstaller : IServiceInstaller
{
    /// <summary>
    /// Registers the message bus services with the dependency injection container.
    /// </summary>
    /// <param name="serviceCollection">The service collection to register services into.</param>
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
