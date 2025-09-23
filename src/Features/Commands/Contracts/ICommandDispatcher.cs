namespace Faster.MessageBus.Features.Commands.Contracts
{
    /// <summary>
    /// Defines the contract for a unified entry point (façade) for sending commands across different communication scopes.
    /// </summary>
    /// <remarks>
    /// An implementation of this interface aggregates all available command scopes, providing a single,
    /// injectable service for all command dispatching needs. The desired scope is selected via its corresponding property.
    /// <example>
    /// Example of using the dispatcher:
    /// <code>
    /// public class MyBusinessService
    /// {
    ///     private readonly ICommandDispatcher _messageHandler;
    ///
    ///     public MyBusinessService(ICommandDispatcher dispatcher)
    ///     {
    ///         _messageHandler = dispatcher;
    ///     }
    ///
    ///     public async Task DoWork()
    ///     {
    ///         // SendAsync a command within the same process
    ///         var response = await _messageHandler.Local.SendAsync(topic, command, timeout);
    ///
    ///         // Broadcast a command to all nodes in the cluster
    ///         await foreach(var clusterResponse in _messageHandler.Cluster.SendAsync(topic, command, timeout))
    ///         {
    ///             // process results from cluster...
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    public interface ICommandDispatcher
    {
        /// <summary>
        /// Gets the command scope for communication with all nodes in the local cluster.
        /// </summary>
        /// <value>The <see cref="IClusterCommandScope"/> implementation.</value>
        ICommandScope Cluster { get; }

        /// <summary>
        /// Gets the command scope for high-performance, in-process communication.
        /// </summary>
        /// <value>The <see cref="ILocalCommandScope"/> implementation.</value>
        ICommandScope Local { get; }

        /// <summary>
        /// Gets the command scope for inter-process communication on the same machine.
        /// </summary>
        /// <value>The <see cref="ICommandScope"/> implementation.</value>
        ICommandScope Machine { get; }

        /// <summary>
        /// Gets the command scope for communication across a wide area network (WAN) to other clusters or remote services.
        /// </summary>
        /// <value>The <see cref="INetworkCommandScope"/> implementation.</value>
        ICommandScope Network { get; }

    }
}