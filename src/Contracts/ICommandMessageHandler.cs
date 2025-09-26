using Faster.MessageBus.Features.Commands;
using System.Buffers;

namespace Faster.MessageBus.Contracts
{
    public interface ICommandMessageHandler
    {
        void AddCommandHandler<TCommand>(ulong topic) where TCommand : ICommand;
        void AddCommandHandlerWithResponse<TCommand, TResponse>(ulong topic) where TCommand : ICommand<TResponse>;
        Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>> GetHandler(ulong topic);
    }
}