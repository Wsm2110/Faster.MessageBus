using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using System.Collections.Concurrent;
using System.Reflection;

namespace Faster.MessageBus.Features.Commands;

internal class CommandHandlerAssemblyScanner : ICommandAssemblyScanner
{
    /// <summary>
    /// Finds all handlers that implement ICommandHandler.
    /// This implementation is optimized for performance by using Parallel LINQ (PLINQ)
    /// to scan assemblies concurrently and is resilient to assembly load errors.
    /// </summary>
    /// <returns>A collection of (command type, response type) pairs. ResponseType is null for commands without a response.</returns>
    public IEnumerable<(Type messageType, Type responseType)> FindAllCommands()
    {
        // Cache the generic type definitions outside the parallel query to avoid repeated lookups.
        var commandHandlerWithResponse = typeof(ICommandHandler<,>);
        var commandHandlerVoid = typeof(ICommandHandler<>);

        // Get all assemblies loaded in the current AppDomain.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        // Use a ConcurrentBag as it's optimized for parallel additions.
        var handlerTypes = new ConcurrentBag<(Type messageType, Type responseType)>();

        // Execute the discovery process in parallel across all available CPU cores.
        Parallel.ForEach(assemblies, assembly =>
        {
            // Use a try-catch block to safely get types from an assembly.
            // This prevents a ReflectionTypeLoadException from crashing the entire application
            // if an assembly has missing dependencies.
            Type[] typesInAssembly;
            try
            {
                typesInAssembly = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                // Silently ignore assemblies that fail to load types.
                // Alternatively, you could log the exception's LoaderExceptions property.
                return; // Skips this assembly
            }

            foreach (var type in typesInAssembly)
            {
                // Quick initial filter: we only care about concrete classes.
                if (!type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                // Iterate through the interfaces implemented by the type.
                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType)
                    {
                        continue;
                    }

                    var genericTypeDef = iface.GetGenericTypeDefinition();

                    // Check if the interface is ICommandHandler<TCommand, TResponse>
                    if (genericTypeDef == commandHandlerWithResponse)
                    {
                        var genericArgs = iface.GetGenericArguments();
                        handlerTypes.Add((messageType: genericArgs[0], responseType: genericArgs[1]));
                        // A class typically won't implement multiple variations for the same handler type,
                        // but we continue just in case of unusual scenarios.
                    }
                    // Check if the interface is ICommandHandler<TCommand>
                    else if (genericTypeDef == commandHandlerVoid)
                    {
                        var genericArgs = iface.GetGenericArguments();
                        handlerTypes.Add((messageType: genericArgs[0], responseType: null));
                    }
                }
            }
        });

        return handlerTypes;
    }

}
