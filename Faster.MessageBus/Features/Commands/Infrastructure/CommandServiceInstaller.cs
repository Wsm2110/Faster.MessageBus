using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Scope.Cluster;
using Faster.MessageBus.Features.Commands.Scope.Local;
using Faster.MessageBus.Features.Commands.Scope.Machine;
using Faster.MessageBus.Features.Commands.Scope.Network;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Infrastructure
{
    internal class CommandServiceInstaller : IServiceInstaller
    {
        public void Install(IServiceCollection serviceCollection)
        {
            // Register and configure MeshMQ _options
            serviceCollection.Configure<MessageBusOptions>(options => { });

            serviceCollection.AddSingleton<LocalEndpoint>();
            serviceCollection.AddSingleton<CommandServer>();
            serviceCollection.AddSingleton<ICommandMessageHandler, CommandMessageHandler>();
            serviceCollection.AddSingleton<ICommandAssemblyScanner, CommandHandlerAssemblyScanner>();

            serviceCollection.AddSingleton<ILocalCommandScope, LocalCommandScope>();
            serviceCollection.AddSingleton<ILocalSocketManager, LocalSocketManager>();
            serviceCollection.AddSingleton<IMachineCommandScope, MachineCommandScope>();
            serviceCollection.AddSingleton<IMachineSocketManager, MachineSocketManager>();
            serviceCollection.AddSingleton<IClusterCommandScope, ClusterCommandScope>();
            serviceCollection.AddSingleton<IClusterSocketManager, ClusterSocketManager>();
            serviceCollection.AddSingleton<INetworkCommandScope, NetworkCommandScope>();
            serviceCollection.AddSingleton<INetworkSocketManager, NetworkSocketManager>();

            serviceCollection.AddSingleton<ICommandScheduler, CommandScheduler>();
            serviceCollection.AddSingleton<ICommandSerializer, CommandSerializer>();
            serviceCollection.AddSingleton<ICommandReplyHandler, CommandReplyHandler>();

        }
    }
}
