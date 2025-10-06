namespace Faster.MessageBus.Contracts 
{
    /// <summary>
    /// Defines the contract for an application or node participating in the distributed
    /// message bus mesh architecture.
    /// </summary>
    /// <remarks>
    /// This contract encapsulates the essential network and routing details required
    /// for mesh communication, including addressing, clustering, and command routing.
    /// </remarks>
    public interface IMeshApplication
    {
        /// <summary>
        /// Gets the network address (e.g., IP address or hostname) where the application is accessible.
        /// </summary>
        string Address { get; } // The network location (IP/hostname) of the application.

        /// <summary>
        /// Gets the unique, user-friendly name of the application.
        /// </summary>
        string ApplicationName { get; } // The name used to identify this application.

        /// <summary>
        /// Gets or sets the name of the cluster this application belongs to.
        /// </summary>
        string ClusterName { get; set; } // The group/cluster that this application is a member of.

        /// <summary>
        /// Gets or sets the routing table used to determine which node should handle a specific command ID.
        /// </summary>
        /// <remarks>
        /// This table likely maps command identifiers to indices or node IDs within the cluster for distributed command execution.
        /// The <see cref="ulong"/> array suggests large capacity for command IDs.
        /// </remarks>
        ulong[] CommandRoutingTable { get; set; } // An array mapping command IDs to routing information (e.g., node index).

        /// <summary>
        /// Gets the network port used for publishing messages (e.g., for pub/sub communication).
        /// </summary>
        ushort PubPort { get; } // The port used for publishing and subscribing to events.

        /// <summary>
        /// Gets the network port used for Remote Procedure Calls (RPC) or request/response commands.
        /// </summary>
        ushort RpcPort { get; } // The port used for direct, synchronous command/request communication.
    }
}