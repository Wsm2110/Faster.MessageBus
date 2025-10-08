using MessagePack;

namespace Faster.MessageBus.Shared;

using System.Diagnostics;
using MessagePack;

/// <summary>
/// Represents the context and identification details of a single node (workstation/application)
/// participating in the mesh network. This structure is intended for serialization 
/// and transmission via NetMQ Beacon signals.
/// </summary>
// The DebuggerDisplay attribute helps visualize key properties when debugging.
[DebuggerDisplay("{Self} url:{ApplicationName}@{WorkstationName} ({Address}:{RpcPort}) - Cluster: {ClusterName}")]
[MessagePackObject] // Marks the struct for MessagePack serialization
public record struct MeshContext
{
    // Default constructor is required for struct initialization, especially with property initializers.
    public MeshContext()
    {
    }

    /// <summary>
    /// The name of the application running on the node.
    /// </summary>
    [property: Key(0)]
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// The host or workstation name where the application is running.
    /// </summary>
    [property: Key(1)]
    public string WorkstationName { get; set; } = string.Empty;

    /// <summary>
    /// The IP address (typically local) of the node.
    /// </summary>
    [property: Key(2)]
    public string Address { get; set; }

    /// <summary>
    /// The port used for RPC (Remote Procedure Call) communication (e.g., using NetMQ REP/REQ sockets).
    /// </summary>
    [property: Key(3)]
    public ushort RpcPort { get; set; }

    /// <summary>
    /// The port used for Publish/Subscribe communication (e.g., using NetMQ PUB/SUB sockets).
    /// </summary>
    [property: Key(4)]
    public ushort PubPort { get; set; }

    /// <summary>
    /// The logical group or cluster this node belongs to.
    /// </summary>
    [property: Key(5)]
    public string ClusterName { get; set; } = string.Empty;

    /// <summary>
    /// A unique identifier for this specific instance of the mesh node.
    /// </summary>
    [property: Key(6)]
    public ulong MeshId { get; set; }

    /// <summary>
    /// A byte array representing the internal command routing table/state.
    /// This may be optional and used for more complex routing logic.
    /// </summary>
    [property: Key(7)]
    public byte[]? CommandRoutingTable { get; set; }

    /// <summary>
    /// 
    /// </summary>
    [property: Key(8)]
    public byte Hosts { get; set; }

    // --- Properties below are for local management and are NOT serialized by MessagePack ---

    /// <summary>
    /// Flag indicating if this context represents the local, executing application.
    /// </summary>
    [IgnoreMember]
    public bool Self { get; set; }

    /// <summary>
    /// The UTC timestamp when this node's signal was last received.
    /// Used for node health monitoring and cleanup.
    /// </summary>
    [IgnoreMember]
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
  
}