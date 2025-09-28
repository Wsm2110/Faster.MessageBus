using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.Reflection;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Manages and dispatches command handlers.
/// </summary>
internal class CommandMessageHandler : ICommandMessageHandler
{
    #region Fields

    // Caching MethodInfo in static fields is efficient; reflection lookup occurs only once.
    private static readonly MethodInfo CreateHandlerMethod =
        typeof(CommandMessageHandler).GetMethod(nameof(CreateHandlerForCommand), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CreateHandlerWithResponseMethod =
        typeof(CommandMessageHandler).GetMethod(nameof(CreateHandlerForCommandWithResponse), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly Dictionary<ulong, Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>>> _commandHandlers = new();

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
    public Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>> GetHandler(ulong topic)
    {
        if (!_commandHandlers.TryGetValue(topic, out var handler))
        {
            throw new KeyNotFoundException($"No command handler registered for topic hash '{topic}'.");
        }
        return handler;
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
        var handlerDelegate = (Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>>)genericFactory.Invoke(null, null)!;

        // Add the compiled delegate to the dictionary.
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

    private static Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>> CreateHandlerForCommand<TCommand>() where TCommand : ICommand
    {
        return async static (serviceProvider, serializer, payload) =>
        {
            var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand>>();
            var command = (TCommand)serializer.Deserialize<ICommand>(payload);
            await handler.Handle(command, CancellationToken.None);
            return Array.Empty<byte>();
        };
    }

    private static Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>> CreateHandlerForCommandWithResponse<TCommand, TResponse>() where TCommand : ICommand<TResponse>
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