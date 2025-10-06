using Microsoft.Extensions.DependencyInjection;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Collections.Generic;
using Xunit;
using System.Linq;

namespace UnitTests;

public class MachineDispatcherTests
{
    [Fact]
    public async Task Machine_SendAsync_single_provider_returns_response()
    {
        // Arrange
        using var machineBase = new MachineBase();

        var b = machineBase.CreateMessageBus();

        await Task.Delay(TimeSpan.FromSeconds(1)); // allow discovery

        // Act
        int count = 0;
        await foreach (var _ in b.Broker.CommandDispatcher.Machine.StreamAsync(new TestPing("hi"), TimeSpan.FromSeconds(5)))
        {
            ++count;
        }

        // Assert
        Assert.True(count >= 1);
    }

    [Fact]
    public async Task Machine_SendAsync_two_providers_returns_two_responses()
    {
        // Arrange
        Action<IServiceCollection> addHandler = services => services.AddTransient<ICommandHandler<TestPing, string>, PongCommandHandler>();
        using var machineBase = new MachineBase();


        var (_, broker1) = machineBase.CreateMessageBus(addHandler);

        _ = machineBase.CreateMessageBus(addHandler); // Create second provider

        await Task.Delay(TimeSpan.FromSeconds(1)); // wait for discovery

        // Act
        int count = 0;
        await foreach (var result in broker1.CommandDispatcher.Machine.StreamResultAsync(new TestPing("hello"), TimeSpan.FromSeconds(1)))
        {
            if (result.IsSuccess)
            {
                count++;
            }
            else
            {

            }
        }

        // Assert
        Assert.Equal(2, count);


    }

    [Fact]
    public async Task Machine_SendAsync_handles_cancellation()
    {
        // Arrange
        using var machineBase = new MachineBase();

        var (_, broker) = machineBase.CreateMessageBus(services =>
        {
            services.AddTransient<ICommandHandler<TestPing, string>, SlowPongCommandHandler>();
        });
        await Task.Delay(TimeSpan.FromSeconds(1)); // allow startup
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act & Assert
        await foreach (var _ in broker.CommandDispatcher.Machine.StreamAsync(new TestPing("timeout"),
            TimeSpan.FromSeconds(2), (ex, target) =>
            {

            }))
        {
            // This loop should be cancelled before it receives a response
        }

    }

    [Fact]
    public async Task Machine_StreamResultAsync_no_handler_returns_no_response()
    {
        // Arrange
        using var machineBase = new MachineBase();

        var (_, broker) = machineBase.CreateMessageBus(); // No handler registered for Smile command
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Act
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Machine.StreamResultAsync(new Smile(), TimeSpan.FromSeconds(2)))
        {
            resp.Match(
                success => ++count,
                error => Console.WriteLine(error.Message)
            );
        }

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Machine_SendAsync_empty_message_returns_response()
    {
        // Arrange
        using var machineBase = new MachineBase();

        var (_, broker) = machineBase.CreateMessageBus(services =>
        {
            services.AddTransient<ICommandHandler<TestPing, string>, PongCommandHandler>();
        });
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Act
        var responses = new List<string>();
        await foreach (var resp in broker.CommandDispatcher.Machine.StreamAsync(new TestPing(string.Empty), TimeSpan.FromSeconds(2)))
        {
            responses.Add(resp);
        }

        // Assert
        Assert.NotEmpty(responses);
    }

    [Fact]
    public async Task Machine_SendAsync_ten_providers_returns_ten_responses()
    {
        // Arrange
        using var machineBase = new MachineBase();

        const int providerCount = 10;
        var brokers = new List<IMessageBroker>();
     
        for (int i = 0; i < providerCount; i++)
        {
            var (_, broker) = machineBase.CreateMessageBus();
            brokers.Add(broker);
        }

        // Give the mesh network time to form among all 10 nodes
        await Task.Delay(TimeSpan.FromSeconds(1));

        var sendingBroker = brokers.First();

        // Act
        int successCount = 0;
        // Set a generous timeout to allow all nodes to respond
        await foreach (var result in sendingBroker.CommandDispatcher.Machine.StreamResultAsync(new TestPing("scale test"), TimeSpan.FromSeconds(10)))
        {
            if (result.IsSuccess)
            {
                successCount++;
            }
            else
            {
                // Log or handle errors if needed
                Console.WriteLine($"Error from target: {result.Match(_ => string.Empty, err => err.Message)}");
            }
        }
        // Assert
        Assert.Equal(providerCount, successCount);
    }

    public record TestPing(string Message) : ICommand<string>;

    public record Smile : ICommand<string>;

    public class PongCommandHandler : ICommandHandler<TestPing, string>
    {
        public Task<string> Handle(TestPing message, CancellationToken cancellationToken)
        {
            return Task.FromResult("pong");
        }
    }

    public class AltPongCommandHandler : ICommandHandler<TestPing, string>
    {
        public Task<string> Handle(TestPing message, CancellationToken cancellationToken)
        {
            return Task.FromResult("altpong");
        }
    }

    public class SlowPongCommandHandler : ICommandHandler<TestPing, string>
    {
        public async Task<string> Handle(TestPing message, CancellationToken cancellationToken)
        {
            return "slow-pong";
        }
    }
}