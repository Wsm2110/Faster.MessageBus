using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VSDiagnostics;

[CPUUsageDiagnoser]
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
        services.AddMessageBus(options =>
        {
            options.ApplicationName = "benchmark_node";
            options.RPCPort = 52345;
        });
        _provider = services.BuildServiceProvider();
        _broker = _provider.GetRequiredService<IMessageBroker>();
        _command = new UserCreatedEvent("BenchmarkUser");
    }

    [Benchmark]
    public async Task SendMachineCommand()
    {
        for (int i = 0; i < 10000; i++)
        {
            await _broker.CommandDispatcher.Machine.SendAsync(_command, TimeSpan.FromSeconds(1));
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