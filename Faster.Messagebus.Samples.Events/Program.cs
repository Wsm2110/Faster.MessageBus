using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;

// --- Application Entry Point ---

// 1. Set up the dependency injection container and register the message bus services.
var builder = new ServiceCollection().AddMessageBus();
var provider = builder.BuildServiceProvider();

// 2. Resolve the main message broker service from the container.
var messageBus = provider.GetRequiredService<IMessageBroker>();

// Fire event...
messageBus.EventDispatcher.Publish(new UserLoggedInEvent("I AM GROOT"));
Console.ReadKey();

/// <summary>
/// Represents a "request-response" command for a greeting.
/// It implements <see cref="ICommand{TResponse}"/> with a type of <see cref="string"/>,
/// signifying that it expects a string response from its handler.
/// </summary>
/// <param name="Name">The name to include in the greeting.</param>
public record UserLoggedInEvent(string Name) : IEvent;

// --- Command Handler Implementations ---

/// <summary>
/// Handles the <see cref="UserCreatedEvent"/>. This is a fire-and-forget handler.
/// Its logic would typically involve side-effects, such as logging or sending a welcome email.
/// </summary>
public class UserLoggedInEventHandler : IEventHandler<UserLoggedInEvent>
{
    public Task Handle(UserLoggedInEvent msg)
    {
        Console.WriteLine($"user {msg.Name} logged in");
        return Task.CompletedTask;
    }
}
