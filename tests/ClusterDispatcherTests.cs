using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using System.Threading.Tasks;
using System;
using System.Threading;
using Faster.MessageBus.Features.Commands.Shared;

namespace UnitTests;

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
        await Task.Delay(2000);

        // send a command via Cluster scope (ICommandScope)
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Cluster.StreamAsync(new Ping("hi"), TimeSpan.FromSeconds(10)))
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
            options.Cluster.ClusterName = "testCluster4";
        });

        var provider = builder.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();
       

        var builder2 = new ServiceCollection().AddMessageBus(options =>
        {
            options.Cluster.ClusterName = "testCluster4";
        });

        var provider2 = builder2.BuildServiceProvider();
        var broker2 = provider2.GetRequiredService<IMessageBroker>();
        
        //wait for discovery
        await Task.Delay(1000);
      
        // send a command via Cluster scope (ICommandScope)
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Cluster.StreamAsync(new Ping("hi"), TimeSpan.FromSeconds(20)))
        {
            ++count;
        }
     
        Assert.Equal(2, count);
    }

    [Fact]
    public async void Cluster_SendAsync_returns_responses()
    {
        // 1. Set up the dependency injection container and register the message bus services.
        var builder = new ServiceCollection().AddMessageBus(options =>
        {
            options.Cluster.ClusterName = "testCluster_";
        });

        var provider = builder.BuildServiceProvider();    
        var broker = provider.GetRequiredService<IMessageBroker>();       
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // send a command via Cluster scope (ICommandScope)
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Cluster.StreamAsync(new Ping("hi"), TimeSpan.FromSeconds(2)))
        {
            ++count;
        }

        provider.Dispose();
        Assert.True(count == 1);
    }

    [Fact]
    public async void Cluster_ApplicationName_SendAsync_returns_responses()
    {
        // 1. Set up the dependency injection container and register the message bus services.
        var builder = new ServiceCollection().AddMessageBus(options =>
        {
            options.ApplicationName = "TestApp";
            options.Cluster.Applications.Add(new Application("TestApp"));
        });

        var provider = builder.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await Task.Delay(TimeSpan.FromSeconds(1));

        // send a command via Cluster scope (ICommandScope)
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Cluster.StreamAsync(new Ping("hi"), TimeSpan.FromSeconds(2)))
        {
            ++count;
        }

        provider.Dispose();
        Assert.True(count == 1);
    }

    [Fact]
    public async void Cluster_Different_ApplicationName_SendAsync_returns_no_response()
    {
        // 1. Set up the dependency injection container and register the message bus services.
        var builder = new ServiceCollection().AddMessageBus(options =>
        {            
            options.Cluster.Applications.Add(new Application("TestApp"));
        });

        var provider = builder.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // send a command via Cluster scope (ICommandScope)
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Cluster.StreamAsync(new Ping("hi"), TimeSpan.FromSeconds(2)))
        {
            ++count;
        }

        provider.Dispose();
        Assert.True(count == 0);
    }

    [Fact]
    public async void Cluster_By_ApplicationName_SendAsync_returns_responses()
    {
        // 1. Set up the dependency injection container and register the message bus services.
        var builder = new ServiceCollection().AddMessageBus(options =>
        {           
            options.Cluster.Applications.Add(new Application("TestApp"));
        });

       using var provider = builder.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();

        var builder2 = new ServiceCollection().AddMessageBus(options =>
        {
            options.ApplicationName = "TestApp";
        });

        using var provider2 = builder2.BuildServiceProvider();
        var broker2 = provider2.GetRequiredService<IMessageBroker>();

        await Task.Delay(TimeSpan.FromSeconds(2));

        // send a command via Cluster scope (ICommandScope)
        int count = 0;
        await foreach (var resp in broker.CommandDispatcher.Cluster.StreamAsync(new Ping("hi"), TimeSpan.FromSeconds(2), (e, destionation) => { }, CancellationToken.None))
        {
            ++count;
        }

        Assert.True(count == 2);
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