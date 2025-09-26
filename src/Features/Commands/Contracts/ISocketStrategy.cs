using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Contracts
{
    public interface ISocketStrategy
    {
        public bool Validate(MeshInfo info, IOptions<MessageBrokerOptions> options);

    }
}