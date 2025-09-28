using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Shared
{
    internal class AddLocalSocketStrategy : ISocketStrategy
    {
        public bool Validate(MeshContext info, IOptions<MessageBrokerOptions> options)
        {
            if (info.Self) 
            {
                return true;
            }

            return false;
        }
    }
}
