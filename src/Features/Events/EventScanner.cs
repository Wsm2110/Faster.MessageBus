using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Finds all registered event handlers by enumerating the IServiceCollection.
/// This approach is significantly more performant than scanning assemblies as it only
/// inspects types that have been explicitly registered in the DI container.
/// </summary>
public class EventScanner(IServiceCollection services) : IEventScanner
{
    /// <summary>
    /// Scans the IServiceCollection for registered event handlers.
    /// </summary>
    /// <returns>A collection of unique event types that have handlers registered.</returns>
    public IEnumerable<Type> ScanForEvents()
    {
        // Cache the generic type definition for IEventHandler<TEvent>.
        var eventHandlerInterface = typeof(IEventHandler<>);

        var eventTypes = new List<Type>();

        // Iterate over every service descriptor registered in the container.
        foreach (var descriptor in services)
        {
            var serviceType = descriptor.ServiceType;

            // Quick filter: we only care about generic types.
            if (!serviceType.IsGenericType)
            {
                continue;
            }

            var genericTypeDef = serviceType.GetGenericTypeDefinition();

            // Check if the registered service is IEventHandler<TEvent>.
            if (genericTypeDef == eventHandlerInterface)
            {
                var genericArgs = serviceType.GetGenericArguments();
                // The first generic argument is the event type itself.
                eventTypes.Add(genericArgs[0]);
            }
        }

        // An event can have multiple handlers. Return only the distinct event types.
        return eventTypes.Distinct();
    }
}