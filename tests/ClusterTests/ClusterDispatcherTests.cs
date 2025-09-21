using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using System.Threading.Tasks;
using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Faster.MessageBus.Features.Commands.Scope.Cluster;

namespace ClusterTests;

public class ClusterDispatcherTests
{

    [Fact]
    public async void Cluster_SendAsync_returns_Responses_While_using_Multiple_Clusters()
    {
        // 1. Set up the dependency injection container and register the message bus services.
        var builder = new ServiceCollection().AddMessageBus(options =>
        {
            options.Cluster.ClusterName = "testCluster1";
        });

        var provider = builder.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();


        var builder2 = new ServiceCollection().AddMessageBus(options =>
        {
            options.Cluster.ClusterName = "testCluster";
        });

        var provider2 = builder2.BuildServiceProvider();
        var broker2 = provider2.GetRequiredService<IMessageBroker>();

        //wait for discovery
        await Task.Delay(1000);

        // send a command via Cluster scope (ICommandScope)
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Cluster.SendAsync(new Ping("hi"), TimeSpan.FromSeconds(20), CancellationToken.None))
        {
            ++count;
        }

        Assert.True(count == 1);
    }

    [Fact]
    public async void Cluster_SendAsync_returns_multipleResponses()
    {
        // 1. Set up the dependency injection container and register the message bus services.
        var builder = new ServiceCollection().AddMessageBus(options =>
        {
            options.Cluster.ClusterName = "testCluster";
        });

        var provider = builder.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();
       

        var builder2 = new ServiceCollection().AddMessageBus(options =>
        {
            options.Cluster.ClusterName = "testCluster";
        });

        var provider2 = builder2.BuildServiceProvider();
        var broker2 = provider2.GetRequiredService<IMessageBroker>();
        
        //wait for discovery
        await Task.Delay(1000);
      
        // send a command via Cluster scope (ICommandScope)
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Cluster.SendAsync(new Ping("hi"), TimeSpan.FromSeconds(20), CancellationToken.None))
        {
            ++count;
        }
     
        Assert.True(count == 2);
    }



    [Fact]
    public async void Cluster_SendAsync_returns_responses()
    {
        // 1. Set up the dependency injection container and register the message bus services.
        var builder = new ServiceCollection().AddMessageBus(options =>
        {
            options.Cluster.ClusterName = "testCluster";
        });

        var provider = builder.BuildServiceProvider();    
        var broker = provider.GetRequiredService<IMessageBroker>();       
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // send a command via Cluster scope (ICommandScope)
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Cluster.SendAsync(new Ping("hi"), TimeSpan.FromSeconds(2), cts.Token))
        {
            ++count;
        }

        provider.Dispose();
        Assert.True(count == 1);
    }

    public record Ping(string Message) : ICommand<string>;

    public class PongCommandHandler : ICommandHandler<Ping, string>
    {
        public Task<string> Handle(Ping message, CancellationToken cancellationToken)
        {
            return Task.FromResult("pong");
        }
    }
}