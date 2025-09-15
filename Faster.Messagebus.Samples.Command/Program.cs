using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using ICommand = Faster.MessageBus.Contracts.ICommand;

var builder = new ServiceCollection().AddMessageBus();
var provider = builder.BuildServiceProvider();
var messageBus = provider.GetRequiredService<IMessageBusEntryPoint>();

await messageBus.CommandDispatcher.Local.SendASync(new UserCreatedEvent(), TimeSpan.FromSeconds(100), CancellationToken.None);

public struct UserCreatedEvent : ICommand;

public class SendWelcomeCommandHandler : ICommandHandler<UserCreatedEvent>
{
    public Task Handle(UserCreatedEvent message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}