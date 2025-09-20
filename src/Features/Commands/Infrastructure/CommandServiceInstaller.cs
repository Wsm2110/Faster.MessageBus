using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Scope.Local;
using Faster.MessageBus.Features.Commands.Scope.Machine;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Faster.MessageBus.Features.Commands.Infrastructure;

internal class CommandServiceInstaller : IServiceInstaller
{
    public void Install(IServiceCollection serviceCollection)
    {
        // Register and configure MeshMQ _options
        serviceCollection.Configure<MessageBrokerOptions>(options => { });

        serviceCollection.AddSingleton<LocalEndpoint>();
        serviceCollection.AddSingleton<CommandServer>();

        serviceCollection.AddSingleton<ICommandMessageHandler, CommandMessageHandler>();
        serviceCollection.AddSingleton<ICommandAssemblyScanner, CommandHandlerAssemblyScanner>();

        // register scopes
        serviceCollection.AddSingleton<ILocalCommandScope, LocalCommandScope>();
        serviceCollection.AddSingleton<ICommandScope, CommandScope>();       
        serviceCollection.AddSingleton<ICommandSerializer, CommandSerializer>();
        serviceCollection.AddSingleton<ICommandReplyHandler, CommandReplyHandler>();

        serviceCollection.AddSingleton<ICommandScheduler, CommandScheduler>();    
        serviceCollection.AddSingleton<ICommandSocketManager, CommandSocketManager>();
        serviceCollection.AddSingleton<ILocalSocketManager, LocalSocketManager>();


    }
}