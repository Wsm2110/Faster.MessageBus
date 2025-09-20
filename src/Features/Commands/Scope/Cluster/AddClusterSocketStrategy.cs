using Faster.MessageBus.Features.Commands.Scope.Machine;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Scope.Cluster
{
    internal class AddClusterSocketStrategy : ISocketStrategy
    {
        public bool Validate(MeshInfo info, IOptions<MessageBrokerOptions> options)
        {
            // Note: This filtering logic may need review. As written, it rejects a node if *any* configured
            // application doesn't match, or if *any* configured node IP doesn't match.
            if (options.Value.Cluster.Applications.Any() && options.Value.Cluster.Applications.Exists(app => app.Name != info.ApplicationName))
            {
                return false;
            }

            if (options.Value.Cluster.Nodes?.Exists(node => node.IpAddress != info.Address) ?? false)
            {
                return false;
            }

            return true;
        }
    }
}
