using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Scope.Machine
{
    internal class AddMachineSocketStrategy : ISocketStrategy
    {
        public bool Validate(MeshInfo info, IOptions<MessageBrokerOptions> options)
        {
            if (info.WorkstationName != Environment.MachineName)
            {
                return false;
            }

            return true;
        }
    }
}