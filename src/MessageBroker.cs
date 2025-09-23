using Faster.MessageBus;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Events;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;

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
///         // SendAsync a command and get a reply
///         var response = await _messageBus.CommandDispatcher.Local.SendAsync(topic, command, timeout);
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public class MessageBroker : IServiceInstaller, IMessageBroker, IDisposable
{
    private bool disposedValue;

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

    private Ilifetime _lifetime;

    /// <summary>
    /// 
    /// </summary>
    public MessageBroker()
    {
        // Used for reflection...
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageBroker"/> class.
    /// </summary>
    /// <param name="eventDispatcher">The concrete event dispatcher implementation.</param>
    /// <param name="commandDispatcher">The concrete command dispatcher implementation.</param>
    public MessageBroker(IEventDispatcher eventDispatcher,
        ICommandDispatcher commandDispatcher, 
        IServiceProvider serviceProvider)
    {
        EventDispatcher = eventDispatcher;
        CommandDispatcher = commandDispatcher;

        // initialize startup classes
        _lifetime = serviceProvider.GetRequiredService<Ilifetime>();
        _lifetime.Initialize();
    }

    /// <inheritdoc/>
    public void Install(IServiceCollection serviceCollection)
    {
        // Registers this entry point as a singleton, making IMessageBroker
        // available for injection throughout the application.
        serviceCollection.AddSingleton<IMessageBroker, MessageBroker>();
        serviceCollection.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        serviceCollection.AddSingleton<IEventDispatcher, EventDispatcher>();
        serviceCollection.AddSingleton<IEventAggregator, EventAggregator>();

        serviceCollection.AddSingleton<Ilifetime, Lifetime>();

    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _lifetime.Destruct();
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~MessageBroker()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}