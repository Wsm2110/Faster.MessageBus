using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Scope.Cluster
{
    internal class AddClusterSocketStrategy : ISocketStrategy
    {
        public bool Validate(MeshContext info, IOptions<MessageBrokerOptions> options)
        {
            if (info.Self)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(options.Value.Cluster.ClusterName) && info.ClusterName == options.Value.Cluster.ClusterName)
            {
                return true;
            }

            // Note: This filtering logic may need review. As written, it rejects a node if *any* configured
            // application doesn't match, or if *any* configured node IP doesn't match.
            if (options.Value.Cluster.Applications.Any() && options.Value.Cluster.Applications.Exists(app => app.Name == info.ApplicationName))
            {
                return true;
            }

            if (options.Value.Cluster.Nodes?.Exists(node => node.IpAddress == info.Address) ?? false)
            {
                return true;
            }

            return false;
        }
    }
}
