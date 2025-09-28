using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Contracts
{
    public interface ISocketStrategy
    {
        public bool Validate(MeshContext info, IOptions<MessageBrokerOptions> options);

    }
}