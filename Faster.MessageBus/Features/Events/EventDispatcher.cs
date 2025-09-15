using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Events;

public class EventDispatcher : IEventDispatcher
{
    private readonly Dictionary<Type, List<Func<IEvent, Task>>> _handlers = new();

    public void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);
        if (!_handlers.ContainsKey(eventType)) _handlers[eventType] = new List<Func<IEvent, Task>>();
        _handlers[eventType].Add(e => handler(e as TEvent));
        Console.WriteLine($"Event handler '{handler.Method.Name}' subscribed to '{eventType.Name}'");
    }

    public async Task PublishAsync(IEvent @event)
    {
        Console.WriteLine($"\n[EventDispatcher] Publishing event: {@event.GetType().Name}");
        if (_handlers.TryGetValue(@event.GetType(), out var handlers))
        {
            foreach (var handler in handlers) await handler(@event);
        }
    }

    public IServiceCollection RegisterEventHandlers(IServiceCollection services, Assembly assembly)
    {
        var consumerTypes = assembly
            .GetTypes()
            .Where(t =>
                t is { IsAbstract: false, IsInterface: false } &&
                t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>)))
            .ToList();

        foreach (var implType in consumerTypes)
        {
            var interfaces = implType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));

            foreach (var @interface in interfaces)
            {
                // resolve consumers by fullname
                services.AddTransient(@interface, implType);
            }
        }

        return services;
    }

}

