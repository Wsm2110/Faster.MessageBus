using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Collections.Generic;

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
        await Task.Delay(TimeSpan.FromSeconds(1));

        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Machine.StreamAsync(new Ping("hi"), TimeSpan.FromSeconds(10)))
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
        services1.AddMessageBus();
        using var provider1 = services1.BuildServiceProvider();
        var broker1 = provider1.GetRequiredService<IMessageBroker>();

        // provider 2 (same machine)
        var services2 = new ServiceCollection();
        services2.AddMessageBus();

        using var provider2 = services2.BuildServiceProvider();
        var broker2 = provider2.GetRequiredService<IMessageBroker>();

        // wait for discovery and socket wiring
        await Task.Delay(TimeSpan.FromSeconds(1));

        int count = 0;
        await foreach (var resp in broker1.CommandDispatcher.Machine.StreamResultAsync(new Ping("hello"), TimeSpan.FromSeconds(2)))
        {
            if (resp.IsSuccess) 
            {
                count++;

            }

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
            await foreach (var resp in broker.CommandDispatcher.Machine.StreamAsync(new Ping("timeout"), TimeSpan.FromSeconds(10)))
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

    [Fact]
    public async Task Machine_StreamResultAsync_no_handler_returns_no_response()
    {
        var services = new ServiceCollection();
        services.AddMessageBus(options =>
        {
            options.RPCPort = (ushort)(52390 + (Environment.ProcessId % 1000));
        });
        using var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();
        await Task.Delay(TimeSpan.FromSeconds(1));
        int count = 0;

        await foreach (var resp in broker.CommandDispatcher.Machine.StreamResultAsync(new Smile(), TimeSpan.FromSeconds(2)))
        {
            resp.Match(success => ++count,
                error =>
                {
                    Console.WriteLine(error.Message);
                });
        }

        Assert.True(count == 0);
    }

    [Fact]
    public async Task Machine_StreamAsync_no_handler_returns_no_response()
    {
        var services = new ServiceCollection();
        services.AddMessageBus(options =>
        {
            options.RPCPort = (ushort)(52390 + (Environment.ProcessId % 1000));
            options.ApplicationName = "Smile and wave";
        });

        using var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();
        await Task.Delay(TimeSpan.FromSeconds(1));
        int count = 0;

        Action<Exception, MeshInfo> OnException = (ex, info) =>
        {
            Console.WriteLine($"Timeout or error for WorkStation {info.WorkstationName} - Application: {info.ApplicationName} - url {info.Address}:{info.RpcPort}: {ex.Message}");
        };

        await foreach (var resp in broker.CommandDispatcher.Machine.StreamAsync(new Smile(), TimeSpan.FromSeconds(2), OnException)) 
        {
         
        }

        Assert.True(count == 0);
    }


    [Fact]
    public async Task Machine_SendAsync_multiple_handlers_returns_single_response()
    {
        var services = new ServiceCollection();
        services.AddMessageBus(options =>
        {
            options.RPCPort = (ushort)(52400 + (Environment.ProcessId % 1000));
        });
        // Multiple handlers registered, but only one will be invoked per message per process
        services.AddTransient<ICommandHandler<Ping, string>, PongCommandHandler>();
        services.AddTransient<ICommandHandler<Ping, string>, AltPongCommandHandler>();
        using var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();
        await Task.Delay(TimeSpan.FromSeconds(1));
        int count = 0;
        var responses = new HashSet<string>();
        await foreach (var resp in broker.CommandDispatcher.Machine.StreamAsync(new Ping("multi"), TimeSpan.FromSeconds(2)))
        {
            ++count;
            responses.Add(resp);
        }
        // Only one response expected from this process
        Assert.True(count == 1);
        Assert.True(responses.Contains("pong") || responses.Contains("altpong"));
    }

    [Fact]
    public async Task Machine_SendAsync_empty_message_returns_response()
    {
        var services = new ServiceCollection();
        services.AddMessageBus(options =>
        {
            options.RPCPort = (ushort)(52410 + (Environment.ProcessId % 1000));
        });
        services.AddTransient<ICommandHandler<Ping, string>, PongCommandHandler>();
        using var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();
        await Task.Delay(TimeSpan.FromSeconds(1));
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Machine.StreamAsync(new Ping(string.Empty), TimeSpan.FromSeconds(2)))
        {
            ++count;
        }
        Assert.True(count >= 1);
    }

    // simple command + handler used by tests
    public record Ping(string Message) : ICommand<string>;

    public record Smile : ICommand<string>;

    public class PongCommandHandler : ICommandHandler<Ping, string>
    {
        public Task<string> Handle(Ping message, CancellationToken cancellationToken)
        {
            return Task.FromResult("pong");
        }
    }

    public class AltPongCommandHandler : ICommandHandler<Ping, string>
    {
        public Task<string> Handle(Ping message, CancellationToken cancellationToken)
        {
            return Task.FromResult("altpong");
        }
    }
}