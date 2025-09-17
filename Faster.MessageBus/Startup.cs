using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Features.Heartbeat.Contracts;
using Faster.MessageBus.Shared;

namespace Faster.MessageBus;

internal interface IStartup
{


}

internal class Startup : IStartup
{
    public Startup(IServiceProvider serviceProvider,
        IEventAggregator eventAggregator,      
        LocalEndpoint endpoint)
    {
        // Note, due to lazy loading we have to preload these services    
        serviceProvider.GetService(typeof(CommandServer));
        serviceProvider.GetService(typeof(IEventDispatcher));     
        serviceProvider.GetService(typeof(ICommandMessageHandler));
        serviceProvider.GetService(typeof(IHeartBeatMonitor));


        // Register self
        var localMesh = new MeshInfo
        {
            Id = WyHash64.Next().ToString(),
            Address = endpoint.Address,
            PubPort = endpoint.PubPort,
            RpcPort = endpoint.RpcPort,   
            ApplicationName = endpoint.ApplicationName,
            WorkstationName = Environment.MachineName
        };

        // notify other socketmanagers
        eventAggregator.Publish(new MeshJoined(localMesh));

        // start discovery once were registered 
        var localSocketManager = serviceProvider.GetService(typeof(ILocalSocketManager)) as ILocalSocketManager;
        localSocketManager?.Initialize();

        // start discovery once were registered 
        var discovery = serviceProvider.GetService(typeof(IMeshDiscoveryService)) as IMeshDiscoveryService;
        discovery?.Start(localMesh);

  
    }
}


