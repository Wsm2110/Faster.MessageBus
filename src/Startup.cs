using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Features.Heartbeat.Contracts;
using Faster.MessageBus.Shared;
using System;
using System.Net;

namespace Faster.MessageBus;

internal interface IStartup
{
    bool IsInitialized { get; }

    void Initialize();
}

internal class Startup(IServiceProvider serviceProvider,
        LocalEndpoint endpoint) : IStartup
{
    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        IsInitialized = true;

        // Note, due to lazy loading we have to preload these services    
        serviceProvider.GetService(typeof(CommandServer));
        serviceProvider.GetService(typeof(ICommandMessageHandler));
        serviceProvider.GetService(typeof(IHeartBeatMonitor));

        // start discovery once were registered 
        var localSocketManager = serviceProvider.GetService(typeof(ILocalSocketManager)) as ILocalSocketManager;
        localSocketManager?.Initialize();

        // start discovery once were registered 
        var discovery = serviceProvider.GetService(typeof(IMeshDiscoveryService)) as IMeshDiscoveryService;
        discovery?.Start(endpoint.GetMesh());
    }

    public bool IsInitialized { get; private set; }
}