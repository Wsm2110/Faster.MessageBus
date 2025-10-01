using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace UnitTests;

public class MessageBusMeshTests
{
    #region Events

    public record UserLoggedInEvent(string Username) : IEvent;
    public record UserLoggedOutEvent(string Username) : IEvent;

    #endregion

    #region Handlers

    public class UserLoggedInHandler : IEventHandler<UserLoggedInEvent>
    {
        public TaskCompletionSource<string> Received { get; } = new();
        public Task Handle(UserLoggedInEvent @event)
        {
            Received.TrySetResult(@event.Username);
            return Task.CompletedTask;
        }
    }

    public class UserLoggedOutHandler : IEventHandler<UserLoggedOutEvent>
    {
        public TaskCompletionSource<string> Received { get; } = new();
        public Task Handle(UserLoggedOutEvent @event)
        {
            Received.TrySetResult(@event.Username);
            return Task.CompletedTask;
        }
    }

    public class AdditionalUserLoggedInHandler : IEventHandler<UserLoggedInEvent>
    {
        public TaskCompletionSource<string> Received { get; } = new();
        public Task Handle(UserLoggedInEvent @event)
        {
            Received.TrySetResult(@event.Username.ToUpper()); // Simulate different handling logic
            return Task.CompletedTask;
        }
    }

    #endregion

    [Fact]
    public async Task MiniMesh_ShouldDeliverEventsToMultipleSubscribers()
    {
        // 1️⃣ Set up DI and register MessageBus
        var services = new ServiceCollection().AddMessageBus(autoScan: false);

        // 2️⃣ Register multiple subscribers for different events
        var loggedInHandler1 = new UserLoggedInHandler();
        var loggedOutHandler = new UserLoggedOutHandler();

        services.AddSingleton<IEventHandler<UserLoggedInEvent>>(loggedInHandler1);
        services.AddSingleton<IEventHandler<UserLoggedOutEvent>>(loggedOutHandler);

        var provider = services.BuildServiceProvider();

        // 3️⃣ Resolve broker
        var messageBus = provider.GetRequiredService<IMessageBroker>();

        // 4️⃣ Publish multiple events
        messageBus.EventDispatcher.Publish(new UserLoggedInEvent("Groot"));
        messageBus.EventDispatcher.Publish(new UserLoggedOutEvent("Rocket"));

        // 5️⃣ Collect results
        var results = await Task.WhenAll(
            loggedInHandler1.Received.Task,
            loggedOutHandler.Received.Task
        );

        // 6️⃣ Assert all subscribers received the correct events
        Assert.Contains("Groot", results); // UserLoggedInHandler   
        Assert.Contains("Rocket", results); // UserLoggedOutHandler
    }

    [Fact]
    public async Task MiniMesh_ShouldDeliverEventsToMultipleSubscribersUsingDifferentMessagebrokers()
    {
        // 1️⃣ Set up DI and register MessageBus
        var services = new ServiceCollection().AddMessageBus(autoScan: false);

        // 2️⃣ Register multiple subscribers for different events
        var loggedInHandler1 = new UserLoggedInHandler();
        var loggedOutHandler = new UserLoggedOutHandler();

        services.AddSingleton<IEventHandler<UserLoggedInEvent>>(loggedInHandler1);
        services.AddSingleton<IEventHandler<UserLoggedOutEvent>>(loggedOutHandler);

        var provider = services.BuildServiceProvider();

        // 3️⃣ Resolve broker
        var messageBus = provider.GetRequiredService<IMessageBroker>();

        // 1️⃣ Set up DI and register MessageBus
        var services2 = new ServiceCollection().AddMessageBus(autoScan: false);

        // 2️⃣ Register multiple subscribers for different events
        var loggedInHandler3 = new UserLoggedInHandler();
        var loggedOutHandler4 = new UserLoggedOutHandler();

        services2.AddSingleton<IEventHandler<UserLoggedInEvent>>(loggedInHandler3);
        services2.AddSingleton<IEventHandler<UserLoggedOutEvent>>(loggedOutHandler4);

        var provider2 = services2.BuildServiceProvider();

        // init second bus
        var messageBus2 = provider2.GetRequiredService<IMessageBroker>();

        //wait for discovery
        await Task.Delay(TimeSpan.FromSeconds(1));

        // 4️⃣ Publish multiple events
        messageBus.EventDispatcher.Publish(new UserLoggedInEvent("Groot"));
        messageBus.EventDispatcher.Publish(new UserLoggedOutEvent("Rocket"));

        // 5️⃣ Collect results
        var results = await Task.WhenAll(
            loggedInHandler1.Received.Task,
            loggedOutHandler.Received.Task
        );

        var results2 = await Task.WhenAll(loggedInHandler3.Received.Task, loggedOutHandler4.Received.Task);

        // 6️⃣ Assert all subscribers received the correct events
        Assert.Contains("Groot", results); // UserLoggedInHandler   
        Assert.Contains("Rocket", results); // UserLoggedOutHandler

        Assert.Contains("Groot", results2); // UserLoggedInHandler   
        Assert.Contains("Rocket", results2); // UserLoggedOutHandler
    }



}