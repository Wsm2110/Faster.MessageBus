using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Faster.MessageBus.Features.Commands;

public delegate Task<byte[]> CommandHandlerDelegate(
       IServiceProvider serviceProvider,
       ICommandSerializer serializer,
       ReadOnlyMemory<byte> payload);

/// <summary>
/// Manages and dispatches command handlers.
/// </summary>
internal class CommandHandlerProvider : ICommandHandlerProvider
{
    #region Fields

    // Caching MethodInfo in static fields is efficient; reflection lookup occurs only once.
    private static readonly MethodInfo CreateHandlerMethod =
        typeof(CommandHandlerProvider).GetMethod(nameof(CreateHandlerForCommand), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CreateHandlerWithResponseMethod =
        typeof(CommandHandlerProvider).GetMethod(nameof(CreateHandlerForCommandWithResponse), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly Dictionary<ulong, CommandHandlerDelegate> _commandHandlers = new();
    private static byte[] _emptyPayload = [];
    #endregion


    public void Initialize(IEnumerable<(Type messageType, Type responseType)> commandTypes)
    {
        foreach (var (commandType, responseType) in commandTypes)
        {
            RegisterHandler(commandType, responseType);
        }
    }

    /// <summary>
    /// Retrieves the handler function for a specific topic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CommandHandlerDelegate? GetHandler(ulong topic)
    {
       return _commandHandlers.TryGetValue(topic, out var handler) ? handler : null;  
    }

    /// <summary>
    /// Uses reflection to create and register a single command handler delegate.
    /// </summary>
    private void RegisterHandler(Type commandType, Type? responseType)
    {
        var topic = GetTopicForType(commandType);

        // Select the appropriate factory method and generic type arguments based on whether there's a response.
        bool hasResponse = responseType != null;
        var factoryMethod = hasResponse ? CreateHandlerWithResponseMethod : CreateHandlerMethod;
        var genericTypes = hasResponse ? new[] { commandType, responseType! } : new[] { commandType };

        // Create the specific generic method (e.g., CreateHandlerForCommand<MyCommand>).
        var genericFactory = factoryMethod.MakeGenericMethod(genericTypes);

        // Invoke the static factory to create the handler delegate.
        var handlerDelegate = (CommandHandlerDelegate)genericFactory.Invoke(null, null)!;

        // TryAdd the compiled delegate to the dictionary.
        if (!_commandHandlers.ContainsKey(topic))
        {
            _commandHandlers[topic] = handlerDelegate;
        }
        else
        {
            // Optionally, log or handle the duplicate registration attempt.
            // For now, we simply ignore it to prevent overwriting existing handlers.
        }
    }

    // This section remains unchanged from the previous refactor.
    #region Handler Factory Methods
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CommandHandlerDelegate CreateHandlerForCommand<TCommand>() where TCommand : ICommand
    {
        return async static (serviceProvider, serializer, payload) =>
        {
            var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand>>();
            var command = (TCommand)serializer.Deserialize<ICommand>(payload);
            await handler.Handle(command, CancellationToken.None);
            return _emptyPayload;
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CommandHandlerDelegate CreateHandlerForCommandWithResponse<TCommand, TResponse>() where TCommand : ICommand<TResponse>
    {
        return async static (serviceProvider, serializer, payload) =>
        {         
                var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();
                var command = (TCommand)serializer.Deserialize<ICommand<TResponse>>(payload);
                var result = await handler.Handle(command, CancellationToken.None);
                return serializer.Serialize(result);         
        };
    }

    private static ulong GetTopicForType(Type type) => WyHash.Hash(type.Name);

    #endregion
}