using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Features.Heartbeat.Contracts;

namespace Faster.MessageBus;

internal interface IStartup
{


}

internal class Startup : IStartup
{
    public Startup(IServiceProvider serviceProvider)
    {
        // Note, due to lazy loading we have to preload these services    
        serviceProvider.GetService(typeof(CommandServer));
        serviceProvider.GetService(typeof(IEventPublisher));
        serviceProvider.GetService(typeof(IEventSubscriber));
        serviceProvider.GetService(typeof(IMeshDiscoveryService));
        serviceProvider.GetService(typeof(IHeartBeatMonitor));
        serviceProvider.GetService(typeof(ICommandMessageHandler));

    }

}


