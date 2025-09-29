using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Finds all registered command handlers by enumerating the IServiceCollection.
/// This approach is significantly more performant than scanning assemblies as it only
/// inspects types that have been explicitly registered in the DI container.
/// </summary>
public class CommandScanner(IServiceCollection services) : ICommandScanner
{
    /// <summary>
    /// Scans the IServiceCollection for registered command handlers.
    /// </summary>
    /// <returns>A collection of tuples, each containing the command type and its corresponding response type (which is null for commands without a response).</returns>
    public List<(Type messageType, Type responseType)> ScanForCommands()
    {
        // Cache the generic type definitions for performance.
        var commandHandlerWithResponse = typeof(ICommandHandler<,>);
        var commandHandlerVoid = typeof(ICommandHandler<>);

        var handlerTypes = new List<(Type messageType, Type responseType)>();

        // Iterate over every service descriptor registered in the container.
        foreach (var descriptor in services)
        {
            // The ServiceType is what was used to register the service,
            // e.g., typeof(ICommandHandler<MyCommand>). This is what we need to inspect.
            var serviceType = descriptor.ServiceType;

            // Quick filter: if it's not a generic type, it can't be one of our handlers.
            if (!serviceType.IsGenericType)
            {
                continue;
            }

            var genericTypeDef = serviceType.GetGenericTypeDefinition();

            // Check if the registered service is ICommandHandler<TCommand, TResponse>
            if (genericTypeDef == commandHandlerWithResponse)
            {
                var genericArgs = serviceType.GetGenericArguments();
                handlerTypes.Add((messageType: genericArgs[0], responseType: genericArgs[1]));
            }
            // Check if the registered service is ICommandHandler<TCommand>
            else if (genericTypeDef == commandHandlerVoid)
            {
                var genericArgs = serviceType.GetGenericArguments();
                // For handlers without a response, the responseType is null.
                handlerTypes.Add((messageType: genericArgs[0], responseType: null));
            }
        }

        return handlerTypes;
    }
}