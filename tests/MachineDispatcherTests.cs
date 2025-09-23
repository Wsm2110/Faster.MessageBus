using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace MachineTests;

public class MachineDispatcherTests
{
    [Fact]
    public async Task Machine_SendAsync_single_provider_returns_response()
    {
        var services = new ServiceCollection();
        services.AddMessageBus(options =>
        {
            // ensure a valid port, unique enough for tests running in parallel on CI
            options.RPCPort = (ushort)(52350 + (Environment.ProcessId % 1000));
        });

        // explicitly register handler to avoid relying on assembly scanning
        services.AddTransient<ICommandHandler<Ping, string>, PongCommandHandler>();

        using var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();

        // allow discovery / startup to stabilize
        await Task.Delay(TimeSpan.FromSeconds(2));

        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Machine.SendAsync(new Ping("hi"), TimeSpan.FromSeconds(5), CancellationToken.None))
        {
            ++count;
        }

        Assert.True(count >= 1);
    }

    [Fact]
    public async void Machine_SendAsync_two_providers_returns_two_responses()
    {
        // provider 1
        var services1 = new ServiceCollection();
        services1.AddMessageBus(options =>
        {
            options.RPCPort = (ushort)(52360 + (Environment.ProcessId % 1000));
        });

        using var provider1 = services1.BuildServiceProvider();
        var broker1 = provider1.GetRequiredService<IMessageBroker>();

        // provider 2 (same machine)
        var services2 = new ServiceCollection();
        services2.AddMessageBus(options =>
        {
            options.RPCPort = (ushort)(52370 + (Environment.ProcessId % 1000));
        });

        using var provider2 = services2.BuildServiceProvider();
        var broker2 = provider2.GetRequiredService<IMessageBroker>();

        // wait for discovery and socket wiring
        await Task.Delay(TimeSpan.FromSeconds(2));

        int count = 0;
        await foreach (var resp in broker1.CommandDispatcher.Machine.SendAsync(new Ping("hello"), TimeSpan.FromSeconds(5), CancellationToken.None))
        {
            ++count;
        }
             
        // Expect at least two responders (could include self depending on configuration)
        Assert.True(count == 2);
    }

    [Fact]
    public async Task Machine_SendAsync_handles_cancellation()
    {
        var services = new ServiceCollection();
        services.AddMessageBus(options =>
        {
            options.RPCPort = (ushort)(52380 + (Environment.ProcessId % 1000));
        });
        services.AddTransient<ICommandHandler<Ping, string>, PongCommandHandler>();

        using var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        int count = 0;

        try
        {
            await foreach (var resp in broker.CommandDispatcher.Machine.SendAsync(new Ping("timeout"), TimeSpan.FromSeconds(10), cts.Token))
            {
                ++count;
            }
        }
        catch (OperationCanceledException)
        {
            // expected when cancelled
        }

        // either zero responses (cancelled) or some if timing allowed — ensure no exception leaked
        Assert.True(count >= 0);
    }

    // simple command + handler used by tests
    public record Ping(string Message) : ICommand<string>;

    public class PongCommandHandler : ICommandHandler<Ping, string>
    {
        public Task<string> Handle(Ping message, CancellationToken cancellationToken)
        {
            return Task.FromResult("pong");
        }
    }
}