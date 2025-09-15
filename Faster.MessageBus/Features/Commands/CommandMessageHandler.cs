using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using System.Buffers;

namespace Faster.MessageBus.Features.Commands;

internal class CommandMessageHandler : ICommandMessageHandler
{

    private readonly Dictionary<ulong, Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>>> _commandHandlers = new();

    public CommandMessageHandler(ICommandAssemblyScanner commandAssemblyScanner)
    {
        var commandTypes = commandAssemblyScanner.FindAllCommands();

        // Use reflection to call the generic AddNotificationHandler<TNotification> method
        var addMethod = typeof(CommandMessageHandler)
            .GetMethod(nameof(AddCommandHandler))!;

        var addMethodResponse = typeof(CommandMessageHandler)
          .GetMethod(nameof(AddCommandHandlerWithResponse))!;

        foreach (var (messageType, responseType) in commandTypes)
        {
            if (responseType == null)
            {
                var genericMethod = addMethod.MakeGenericMethod(messageType);

                // By convention, use the message type's name as the topic.
                // This could be made more flexible, e.g., by using a custom attribute on the message class.
                genericMethod.Invoke(this, new object[]
                {
                    WyHashHelper.Hash(messageType.Name)
                });
                continue;
            }

            var genericMethodResponse = addMethodResponse.MakeGenericMethod(messageType, responseType);

            // By convention, use the message type's name as the topic.
            // This could be made more flexible, e.g., by using a custom attribute on the message class.
            genericMethodResponse.Invoke(this, new object[]
            {
                   WyHashHelper.Hash(messageType.Name)
            });
        }
    }

    public void AddCommandHandler<TCommand>(ulong topic) where TCommand : ICommand
    {
        _commandHandlers[topic] = async static (serviceProvider, serializer, payload) =>
        {
            // 2. Resolve IMessageHandler<TCommand, TResponse> dynamically
            var handler = serviceProvider.GetService(typeof(ICommandHandler<TCommand>)) as ICommandHandler<TCommand>;
            if (handler == null)
            {
                throw new InvalidOperationException($"No handler registered for {typeof(TCommand)}");
            }

            var message = serializer.Deserialize<TCommand>(payload);
            await handler.Handle(message, CancellationToken.None);

            return null;
        };
    }

    public void AddCommandHandlerWithResponse<TCommand, TResponse>(ulong topic) where TCommand : ICommand<TResponse>
    {
        _commandHandlers[topic] = async static (serviceProvider, serializer, payload) =>
        {
            // 2. Resolve IMessageHandler<TCommand, TResponse> dynamically
            var handler = serviceProvider.GetService(typeof(ICommandHandler<TCommand, TResponse>)) as ICommandHandler<TCommand, TResponse>;
            if (handler == null)
            {
                throw new InvalidOperationException($"No handler registered for {typeof(TCommand)}");
            }

            var message = (TCommand)serializer.Deserialize<ICommand<TResponse>>(payload);

            var result = await handler.Handle(message, CancellationToken.None);

            return serializer.Serialize(result);
        };
    }

    public Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>> GetHandler(ulong topic) => _commandHandlers[topic];




}

