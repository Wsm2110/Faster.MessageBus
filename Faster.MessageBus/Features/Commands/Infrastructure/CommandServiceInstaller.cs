using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Scope.Cluster;
using Faster.MessageBus.Features.Commands.Scope.Local;
using Faster.MessageBus.Features.Commands.Scope.Machine;
using Faster.MessageBus.Features.Commands.Scope.Network;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Infrastructure;

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

        // register scopes
        serviceCollection.AddSingleton<ILocalCommandScope, LocalCommandScope>();
        serviceCollection.AddSingleton<IMachineCommandScope, MachineCommandScope>();
        serviceCollection.AddSingleton<IClusterCommandScope, ClusterCommandScope>();
        serviceCollection.AddSingleton<INetworkCommandScope, NetworkCommandScope>();
        serviceCollection.AddSingleton<ICommandSerializer, CommandSerializer>();
        serviceCollection.AddSingleton<ICommandReplyHandler, CommandReplyHandler>();

        serviceCollection.AddKeyedSingleton<ICommandScheduler, LocalCommandScheduler>("localCommandScheduler", (provider, key)
 => new LocalCommandScheduler("localCommandScheduler"));

        serviceCollection.AddKeyedSingleton<ICommandScheduler, MachineCommandScheduler>("machineCommandScheduler", (provider, key)
            => new MachineCommandScheduler("machineCommandScheduler"));

        serviceCollection.AddKeyedSingleton<ICommandScheduler, ClusterCommandScheduler>("clusterCommandScheduler", (provider, key)
     => new ClusterCommandScheduler("clusterCommandScheduler"));

        serviceCollection.AddKeyedSingleton<ICommandScheduler, NetworkCommandScheduler>("networkCommandScheduler", (provider, key)
     => new NetworkCommandScheduler("networkCommandScheduler"));

        serviceCollection.AddSingleton<INetworkSocketManager, NetworkSocketManager>();
        serviceCollection.AddSingleton<IClusterSocketManager, ClusterSocketManager>();
        serviceCollection.AddSingleton<IMachineSocketManager, MachineSocketManager>();
        serviceCollection.AddSingleton<ILocalSocketManager, LocalSocketManager>();


    }
}