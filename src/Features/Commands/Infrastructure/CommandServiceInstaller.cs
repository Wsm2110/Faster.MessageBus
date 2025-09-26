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

        serviceCollection.AddSingleton<LocalEndpoint>();
        serviceCollection.AddSingleton<CommandServer>();

        serviceCollection.AddSingleton<ICommandMessageHandler, CommandMessageHandler>();
        serviceCollection.AddSingleton<ICommandAssemblyScanner, CommandHandlerAssemblyScanner>();

    
        serviceCollection.AddSingleton<ICommandSerializer, CommandSerializer>();
        serviceCollection.AddSingleton<ICommandReplyHandler, CommandReplyHandler>();

        serviceCollection.AddScoped<ICommandScope, CommandScope>();
        serviceCollection.AddScoped<ICommandProcessor, CommandProcessor>();    

    }
}