using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Faster.MessageBus.Features.Events.Infrastructure;

public class EventInstaller : IServiceInstaller
{
    public void Install(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IEventHandlerProvider, EventHandlerProvider>();
        serviceCollection.AddSingleton<IEventScanner, EventScanner>(sp =>
        {
            return new EventScanner(serviceCollection);
        });
        serviceCollection.AddSingleton<IEventSerializer, EventSerializer>();
        serviceCollection.AddSingleton<IEventDispatcher, EventDispatcher>();
        serviceCollection.AddSingleton<IEventReceivedHandler, EventReceivedHandler>();
        serviceCollection.AddSingleton<IEventScheduler, EventScheduler>();
        serviceCollection.AddSingleton<IEventSocketManager, EventSocketManager>();
    }
}

