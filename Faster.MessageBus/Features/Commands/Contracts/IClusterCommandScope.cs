using Faster.MessageBus.Contracts;

namespace Faster.MessageBus.Features.Commands.Contracts
{
    public interface IClusterCommandScope
    {
        IAsyncEnumerable<TResponse> Send<TResponse>(ulong topic, ICommand<TResponse> command, TimeSpan timeout, CancellationToken ct = default);
        Task SendASync(ulong topic, ICommand command, TimeSpan timeout, CancellationToken ct = default);
    }
}