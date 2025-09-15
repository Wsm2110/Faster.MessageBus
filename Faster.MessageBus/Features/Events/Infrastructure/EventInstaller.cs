using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Faster.MessageBus.Features.Events.Infrastructure;

public class EventInstaller : IServiceInstaller
{
    public void Install(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IEventHandlerProvider, EventHandlerProvider>();
        serviceCollection.AddSingleton<IEventHandlerAssemblyScanner, EventHandlerAssemblyScanner>();
        serviceCollection.AddSingleton<IEventPublisher, EventPublisher>();
        serviceCollection.AddSingleton<IEventSubscriber, EventSubscriber>();
        serviceCollection.AddSingleton<IEventSerializer, EventSerializer>();


    }
}

