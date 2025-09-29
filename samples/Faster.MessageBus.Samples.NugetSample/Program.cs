using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;

// 1. Set up the dependency injection container and register the message bus services.
var services = new ServiceCollection();

// Register your message bus services
services.AddMessageBus();

var provider = services.BuildServiceProvider();

// 2. Resolve the main message broker service from the container.
var messageBus = provider.GetRequiredService<IMessageBroker>();

await Task.Delay(TimeSpan.FromSeconds(1));

await foreach (var response in messageBus.CommandDispatcher.Machine.StreamResultAsync(new HelloCommand("I AM GROOT"), TimeSpan.FromSeconds(1)))
{
    if (response.IsSuccess)
    {
        Console.WriteLine($"[Machine] Response: {response.Value}");
    }
    else
    {
        Console.WriteLine($"[Machine] Error: {response.Error?.Message}");
    }
}

Console.ReadLine();

/// <summary>
/// 
/// </summary>
/// <param name="Name"></param>
public record HelloCommand(string Name) : ICommand<string>;

/// <summary>
/// 
/// </summary>
public class HelloCommandHandler : ICommandHandler<HelloCommand, string>
{
    public Task<string> Handle(HelloCommand command, CancellationToken cancellationToken)
    {
        return Task.FromResult($"Hello, {command.Name}!");
    }
}   