using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Contracts
{
    public interface ISocketStrategy
    {
        public bool Validate(MeshContext context, IOptions<MessageBrokerOptions> options);
    }
}