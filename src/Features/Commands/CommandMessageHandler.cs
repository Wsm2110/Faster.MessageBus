using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using System.Buffers;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Manages and dispatches command handlers.
/// This class scans for command types, registers them, and provides a mechanism to retrieve and execute the correct handler for a given command.
/// </summary>
internal class CommandMessageHandler : ICommandMessageHandler
{
    /// <summary>
    /// A dictionary mapping a topic hash to a handler function.
    /// The function takes a service provider, a serializer, and the payload, and returns the serialized response.
    /// </summary>
    private readonly Dictionary<ulong, Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>>> _commandHandlers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandMessageHandler"/> class.
    /// It scans the provided assemblies for types implementing <see cref="ICommand"/> or <see cref="ICommand{TResponse}"/>
    /// and registers them as handlers.
    /// </summary>
    /// <param name="commandAssemblyScanner">The scanner used to find command types in assemblies.</param>
    public CommandMessageHandler(ICommandAssemblyScanner commandAssemblyScanner)
    {
        // Find all command and their optional response types from the assemblies.
        var commandTypes = commandAssemblyScanner.FindAllCommands();

        // Get MethodInfo for the generic handler registration methods using reflection.
        var addMethod = typeof(CommandMessageHandler)
            .GetMethod(nameof(AddCommandHandler))!;

        var addMethodResponse = typeof(CommandMessageHandler)
          .GetMethod(nameof(AddCommandHandlerWithResponse))!;

        // Iterate over the discovered command types and register a handler for each.
        foreach (var (messageType, responseType) in commandTypes)
        {
            // Register handlers for commands without a response.
            if (responseType == null)
            {
                // Create a generic method instance for the specific command type.
                var genericMethod = addMethod.MakeGenericMethod(messageType);

                // By convention, use the hashed message type's name as the topic.
                // This could be made more flexible, e.g., by using a custom attribute on the message class.
                genericMethod.Invoke(this, new object[]
                {
                    WyHash.Hash(messageType.Name)
                });
                continue;
            }

            // Register handlers for commands with a response.
            var genericMethodResponse = addMethodResponse.MakeGenericMethod(messageType, responseType);

            // By convention, use the hashed message type's name as the topic.
            genericMethodResponse.Invoke(this, new object[]
            {
                   WyHash.Hash(messageType.Name)
            });
        }
    }

    /// <summary>
    /// Adds a handler for a command that does not return a response.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command, which must implement <see cref="ICommand"/>.</typeparam>
    /// <param name="topic">The unique identifier (hash) for the command topic.</param>
    public void AddCommandHandler<TCommand>(ulong topic) where TCommand : ICommand
    {
        _commandHandlers[topic] = async static (serviceProvider, serializer, payload) =>
        {
            // Resolve the specific command handler from the dependency injection container.
            var handler = serviceProvider.GetService(typeof(ICommandHandler<TCommand>)) as ICommandHandler<TCommand>;
            if (handler == null)
            {
                throw new InvalidOperationException($"No handler registered for {typeof(TCommand)}");
            }

            // Deserialize the payload into the command object.
            var message = (TCommand)serializer.Deserialize<ICommand>(payload);

            // Execute the handler.
            await handler.Handle(message, CancellationToken.None);

            // Return an empty byte array as there is no response.
            return Array.Empty<byte>();
        };
    }

    /// <summary>
    /// Adds a handler for a command that returns a response.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command, which must implement <see cref="ICommand{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    /// <param name="topic">The unique identifier (hash) for the command topic.</param>
    public void AddCommandHandlerWithResponse<TCommand, TResponse>(ulong topic) where TCommand : ICommand<TResponse>
    {
        _commandHandlers[topic] = async static (serviceProvider, serializer, payload) =>
        {
            // Resolve the specific command handler from the dependency injection container.
            var handler = serviceProvider.GetService(typeof(ICommandHandler<TCommand, TResponse>)) as ICommandHandler<TCommand, TResponse>;
            if (handler == null)
            {
                throw new InvalidOperationException($"No handler registered for {typeof(TCommand)}");
            }

            // Deserialize the payload into the command object.
            var message = (TCommand)serializer.Deserialize<ICommand<TResponse>>(payload);

            // Execute the handler and get the result.
            var result = await handler.Handle(message, CancellationToken.None);

            // Serialize the result and return it.
            return serializer.Serialize(result);
        };
    }

    /// <summary>
    /// Retrieves the handler function for a specific topic.
    /// </summary>
    /// <param name="topic">The unique identifier for the command topic.</param>
    /// <returns>A function that, when executed, processes the command and returns a serialized response.</returns>
    public Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>> GetHandler(ulong topic)
    {
        _commandHandlers.TryGetValue(topic, out var x);
        // TODO LOG when x is null
        return x;
    }

}