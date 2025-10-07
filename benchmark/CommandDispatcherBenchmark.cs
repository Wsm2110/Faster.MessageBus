using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;

[MemoryDiagnoser]
public class CommandDispatcherBenchmark
{
    private IServiceProvider _provider;
    private IMessageBroker _broker;   
    private ICommand _command;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddMessageBus();
        _provider = services.BuildServiceProvider();
        _broker = _provider.GetRequiredService<IMessageBroker>();
        _command = new UserCreatedEvent("BenchmarkUser");
    }

    [Benchmark]
    public async Task SendMachineCommand()
    {
        var scope = _broker.CommandDispatcher.Machine;
        for (int i = 0; i < 10000; i++)
        {
           await scope.SendAsync(_command, TimeSpan.FromMilliseconds(100000));
        }
    }
}

public record UserCreatedEvent(string Name) : ICommand;

public class UserCreatedEventHandler : ICommandHandler<UserCreatedEvent>
{
    public Task Handle(UserCreatedEvent message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}