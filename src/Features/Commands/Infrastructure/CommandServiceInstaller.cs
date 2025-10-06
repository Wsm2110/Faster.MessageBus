using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Faster.MessageBus.Features.Commands.Infrastructure;

internal class CommandServiceInstaller : IServiceInstaller
{
    public void Install(IServiceCollection serviceCollection)
    {
        // Register and configure MeshMQ _options
        serviceCollection.Configure<MessageBrokerOptions>(options => { });

        serviceCollection.AddSingleton<MeshApplication>();
        serviceCollection.AddSingleton<CommandServer>();

        serviceCollection.AddSingleton<ICommandHandlerProvider, CommandHandlerProvider>();
        serviceCollection.AddSingleton<ICommandScanner, CommandScanner>(p =>
        {
            // instead of scanning all assemblies, we abuse servicecollections to register commandhandlers
            return new CommandScanner(serviceCollection);
        });

        serviceCollection.AddSingleton<ICommandRoutingFilter, CommandRoutingFilter>();

        serviceCollection.AddSingleton<ICommandSerializer, CommandSerializer>();
        serviceCollection.AddSingleton<ICommandResponseHandler, CommandResponseHandler>();

        serviceCollection.AddScoped<ICommandScope, CommandScope>();
        serviceCollection.AddScoped<ICommandSocketManager, CommandSocketManager>();

    }
}