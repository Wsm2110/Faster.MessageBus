using Faster.MessageBus.Features.Commands.Contracts;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Acts as a unified entry point or façade for sending commands across different communication scopes.
/// </summary>
/// <remarks>
/// This class aggregates all available command scopes (Local, Machine, Cluster, Network),
/// providing a single, injectable service for command dispatching. The desired scope is selected
/// via its corresponding property.
/// <example>
/// How to use the dispatcher:
/// <code>
/// // To send a command and get a single reply from within the same process:
/// var localResponse = await dispatcher.Local.SendAsync(topic, command, timeout);
///
/// // To broadcast a command to all nodes in the cluster and stream replies:
/// await foreach(var clusterResponse in dispatcher.Cluster.SendAsync(topic, command, timeout))
/// {
///     // Process each response from the cluster
/// }
/// </code>
/// </example>
/// </remarks>
public class CommandDispatcher(
    ILocalCommandScope LocalCommandScope,
    IMachineCommandScope MachineCommandScope,
    IClusterCommandScope ClusterCommandScope,
    INetworkCommandScope NetworkCommandScope) : ICommandDispatcher
{
    /// <summary>
    /// Gets the command scope for high-performance, in-process communication.
    /// </summary>
    /// <value>The <see cref="ILocalCommandScope"/> implementation.</value>
    public ILocalCommandScope Local { get; } = LocalCommandScope;

    /// <summary>
    /// Gets the command scope for inter-process communication on the same machine.
    /// </summary>
    /// <value>The <see cref="IMachineCommandScope"/> implementation.</value>
    public IMachineCommandScope Machine { get; } = MachineCommandScope;

    /// <summary>
    /// Gets the command scope for communication with all nodes in the local cluster.
    /// </summary>
    /// <value>The <see cref="IClusterCommandScope"/> implementation.</value>
    public IClusterCommandScope Cluster { get; } = ClusterCommandScope;

    /// <summary>
    /// Gets the command scope for communication across a wide area network (WAN) to other clusters or remote services.
    /// </summary>
    /// <value>The <see cref="INetworkCommandScope"/> implementation.</value>
    public INetworkCommandScope Network { get; } = NetworkCommandScope;
}

