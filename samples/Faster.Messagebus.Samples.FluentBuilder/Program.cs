using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;

using ICommand = Faster.MessageBus.Contracts.ICommand;

var builder = new ServiceCollection().AddMessageBus(options =>
{
    options.ApplicationName = "node1";
});
var provider = builder.BuildServiceProvider();

// 2. Resolve the main message broker service from the container.
var messageBus = provider.GetRequiredService<IMessageBroker>();

// need to wait a moment for the discovery to start
await Task.Delay(1000);

await messageBus.CommandDispatcher.Machine.Prepare(new builderCommand())
      .WithTimeout(TimeSpan.FromSeconds(5))
      .SendAsync();

Console.WriteLine("Press Enter to Continue");
Console.ReadLine();

await foreach (var response in messageBus.CommandDispatcher.Machine.Prepare(new builderResponseCommand())
      .WithTimeout(TimeSpan.FromSeconds(5))
      .StreamResultAsync())
{
    if (response.IsSuccess)
    {
        Console.WriteLine(response.Value);
    }
}

Console.WriteLine("Press Enter to exit");
Console.ReadLine();

public record builderCommand : ICommand;

public record builderResponseCommand : ICommand<string>;

public class builderDemoHandler : ICommandHandler<builderCommand>
{
    public Task Handle(builderCommand command, CancellationToken cts)
    {
        return Task.CompletedTask;
    }
}

public class builderResponseHandler : ICommandHandler<builderResponseCommand, string>
{
    public Task<string> Handle(builderResponseCommand command, CancellationToken cts)
    {
        return Task.FromResult("response from builderResponseHandler");
    }
}