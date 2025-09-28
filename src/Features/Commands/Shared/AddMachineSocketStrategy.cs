using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Shared
{
    internal class AddMachineSocketStrategy : ISocketStrategy
    {
        public bool Validate(MeshContext info, IOptions<MessageBrokerOptions> options)
        {          

            if (info.WorkstationName != Environment.MachineName)
            {
                return false;
            }

            return true;
        }
    }
}