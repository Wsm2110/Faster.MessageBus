using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Commands.Scope.Cluster;

/// <summary>
/// Represents a single, addressable node (e.g., a server or container) within a cluster.
/// </summary>
/// <param name="Hostname">The network-resolvable hostname or IP address of the node.</param>
public record Node(string Hostname, string IpAddress);

/// <summary>
/// Represents a specific application or service type within the cluster that can be targeted for commands.
/// </summary>
/// <param name="Name">The logical name of the application or service (e.g., "OrderProcessor", "AuthService").</param>
public record Application(string Name);

/// <summary>
/// Provides filtering criteria for targeting specific nodes or applications within a cluster when sending a command.
/// </summary>
/// <remarks>
/// This class is used to narrow the scope of a cluster-level command. By populating the <see cref="Nodes"/> or
/// <see cref="Applications"/> lists, you can direct a command to a subset of the cluster instead of broadcasting it to all members.
/// If both lists are null or empty, the default behavior is typically to broadcast to the entire cluster.
/// </remarks>
internal class ClusterOptions
{
    /// <summary>
    /// Gets or sets a list of specific nodes to target.
    /// </summary>
    /// <value>
    /// A list of <see cref="Node"/> objects. If populated, the command will only be sent
    /// to nodes with matching hostnames in this list.
    /// </value>
    public List<Node>? Nodes { get; set; } = new List<Node>();

    /// <summary>
    /// Gets or sets a list of specific applications to target.
    /// </summary>
    /// <value>
    /// A list of <see cref="Application"/> objects. If populated, the command will be sent
    /// to all nodes known to be running any of the specified applications.
    /// </value>
    public List<Application>? Applications { get; set; } = new List<Application>();
}