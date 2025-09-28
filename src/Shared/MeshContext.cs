using MessagePack;

namespace Faster.MessageBus.Shared;

[MessagePackObject]
public record struct MeshContext
{
    public MeshContext()
    {
    }

    [property: Key(0)]
    public string ApplicationName { get; set; } = string.Empty;

    [property: Key(1)]
    public string WorkstationName { get; set; } = string.Empty;

    [property: Key(2)]
    public string Address { get; set; }

    [property: Key(3)]
    public ushort RpcPort { get; set; }

    [property: Key(4)]
    public ushort PubPort { get; set; }

    [property: Key(5)]
    public string ClusterName { get; set; } = string.Empty;

    [property: Key(6)]
    public ulong MeshId { get; set; }

    [property: Key(7)]
    public byte[]? CommandRoutingTable { get; set; }

    [IgnoreMember]
    public bool Self { get; set; }

    [IgnoreMember]
    public DateTime LastSeen { get; init; } = DateTime.UtcNow;

}