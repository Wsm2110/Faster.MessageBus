using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using ICommand = Faster.MessageBus.Contracts.ICommand;

// 1. Set up the dependency injection container and register the message bus services.
var builder = new ServiceCollection().AddMessageBus(options =>
{
    options.ApplicationName = "node1";
    options.RPCPort = 52345;
});
var provider = builder.BuildServiceProvider();

// 2. Resolve the main message broker service from the container.
var messageBus = provider.GetRequiredService<IMessageBroker>();

await Task.Delay(TimeSpan.FromSeconds(1));

// 3. StreamAsync a command which will return without result indicating the other end received and processed our command
// This command implements ICommand, so it does not expect a reply.
// The call will complete once the command is dispatched.
await messageBus.CommandDispatcher.Local.SendAsync(new UserCreatedEvent("I AM GROOT Local"), TimeSpan.FromSeconds(5));

//var result = await messageBus.CommandDispatcher.Local.StreamAsync(new HelloEvent("I AM GROOT"), TimeSpan.FromSeconds(1), CancellationToken.None);

// 4. StreamAsync a "request-response" command.
// This command implements ICommand<string>, indicating it expects a string in return.
// The 'await' will pause execution until a response is received or a timeout occurs.
//await messageBus.CommandDispatcher.Machine.StreamAsync(new HelloEventNoResponse("I AM"), TimeSpan.FromSeconds(1), CancellationToken.None);

await Task.Delay(TimeSpan.FromSeconds(1));
Console.WriteLine("start");
int counter = 0;

while (counter < 100000)
{
    try
    {
        await foreach (var response in messageBus.CommandDispatcher.Machine.StreamAsync(new HelloEvent("I AM GROOT Machine"), TimeSpan.FromSeconds(1)))
        {
           Console.WriteLine(response);
        }
    }
    catch (Exception)
    {
       // Console.WriteLine("timeout");
    }
    ++counter;
    
}

// 5. Print the result from the request-response command.
//Console.WriteLine(result); // Expected output: "Hello from I AM GROOT"
Console.ReadKey();


// --- Command/Event Definitions ---

/// <summary>
/// Represents a "fire-and-forget" command indicating a user has been created.
/// It implements <see cref="ICommand"/>, signifying that it does not return a value.
/// </summary>
/// <param name="Name">The name of the user that was created.</param>
public record UserCreatedEvent(string Name) : ICommand;

/// <summary>
/// Represents a "request-response" command for a greeting.
/// It implements <see cref="ICommand{TResponse}"/> with a type of <see cref="string"/>,
/// signifying that it expects a string response from its handler.
/// </summary>
/// <param name="Name">The name to include in the greeting.</param>
public record HelloEvent(string Name) : ICommand<string>;

public record HelloEventNoResponse(string Name) : ICommand;

// --- Command Handler Implementations ---

/// <summary>
/// Handles the <see cref="UserCreatedEvent"/>. This is a fire-and-forget handler.
/// Its logic would typically involve side-effects, such as logging or sending a welcome email.
/// </summary>
public class SendWelcomeCommandHandler : ICommandHandler<UserCreatedEvent>
{
    /// <summary>
    /// Processes the incoming <see cref="UserCreatedEvent"/> message.
    /// </summary>
    /// <param name="message">The command data.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous handling operation.</returns>
    public Task Handle(UserCreatedEvent message, CancellationToken cancellationToken)
    {
        // In a real application, you might send a welcome email here.
        Console.WriteLine($"Welcome handler processed for: {message.Name} {Environment.ProcessId}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Handles the <see cref="HelloEvent"/> and provides a string response.
/// This is a request-response handler.
/// </summary>
public class HelloCommandHandler : ICommandHandler<HelloEvent, string>
{
    /// <summary>
    /// Processes the incoming <see cref="HelloEvent"/> message and generates a reply.
    /// </summary>
    /// <param name="message">The command data.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task{TResult}"/> containing the string response.</returns>
    public Task<string> Handle(HelloEvent message, CancellationToken cancellationToken)
    {
        string response = $"Hello from {message.Name}/* {Environment.MachineName}  {Environment.ProcessId}*/";
        return Task.FromResult(response);
    }
}

public class HelloCommandNoResponseHandler : ICommandHandler<HelloEventNoResponse>
{
    /// <summary>
    /// Processes the incoming <see cref="HelloEvent"/> message and generates a reply.
    /// </summary>
    /// <param name="message">The command data.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task{TResult}"/> containing the string response.</returns>
    public Task Handle(HelloEventNoResponse message, CancellationToken cancellationToken)
    {
        Console.WriteLine("Received call to HelloCommandNoResponseHandler ");
        return Task.CompletedTask;
    }
}