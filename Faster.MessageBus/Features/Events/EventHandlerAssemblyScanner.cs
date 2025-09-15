using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Events.Contracts;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Discovers consumer types by scanning all loaded assemblies using reflection.
/// </summary>
internal class EventHandlerAssemblyScanner : IEventHandlerAssemblyScanner
{
    public IEnumerable<(Type consumerImpl, Type messageType)> FindEventTypes()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        return assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t =>
                t.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
                    .Select(i => (ConsumerImpl: t, MessageType: i.GetGenericArguments()[0])));
    }
}