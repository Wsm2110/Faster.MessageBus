using Faster.MessageBus.Contracts;

namespace Faster.MessageBus.Features.Commands.Contracts
{
    public interface IClusterCommandScope
    {
        IAsyncEnumerable<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, TimeSpan timeout, CancellationToken ct = default);
        Task SendAsync(ICommand command, TimeSpan timeout, CancellationToken ct = default);
    }
}