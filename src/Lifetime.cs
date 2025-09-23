using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Features.Heartbeat.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;

namespace Faster.MessageBus;

internal interface Ilifetime
{
    bool IsInitialized { get; }

    void Initialize();

    void Destruct();
}

internal class Lifetime(IServiceProvider serviceProvider, IEventAggregator eventAggregator, LocalEndpoint endpoint) : Ilifetime
{
    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        IsInitialized = true;

        // Note, due to lazy loading we have to preload these services    
        serviceProvider.GetRequiredService<CommandServer>();
        serviceProvider.GetRequiredService<ICommandMessageHandler>();
        _heartbeatMonitor = serviceProvider.GetRequiredService<IHeartBeatMonitor>();

        // start discovery once were registered 
        _discovery = serviceProvider.GetRequiredService<IMeshDiscoveryService>();
        _discovery?.Start(endpoint.GetMesh());
    }

    public void Destruct()
    {
        if (!IsInitialized)
        {
            return;
        }

        IsInitialized = true;
        eventAggregator.Dispose();

        _heartbeatMonitor?.Stop();
        _heartbeatMonitor?.Dispose();
        // start discovery once were registered 
        _discovery?.Stop();
        _discovery?.Dispose();
    }

    public bool IsInitialized { get; private set; }
    private IHeartBeatMonitor _heartbeatMonitor;
    private IMeshDiscoveryService _discovery;

}