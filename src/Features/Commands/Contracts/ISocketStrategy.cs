using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Scope.Machine
{
    public interface ISocketStrategy
    {
        public bool Validate(MeshInfo info, IOptions<MessageBrokerOptions> options);

    }
}