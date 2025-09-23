using Faster.MessageBus.Features.Commands.Scope.Machine;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Scope.Local
{
    internal class AddLocalSocketStrategy : ISocketStrategy
    {
        public bool Validate(MeshInfo info, IOptions<MessageBrokerOptions> options)
        {
            if (info.Self) 
            {
                return true;
            }

            return false;
        }
    }
}
