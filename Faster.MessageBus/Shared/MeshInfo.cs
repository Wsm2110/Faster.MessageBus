using MessagePack;

namespace Faster.MessageBus.Shared;

[MessagePackObject]
public record struct MeshInfo()
{
    [property: Key(0)]
    public string ApplicationName { get; set; }

    [property: Key(1)]
    public string WorkstationName { get; set; }

    [property: Key(2)]
    public string Address { get; set; }

    [property: Key(3)]
    public ushort RpcPort { get; set; }

    [property: Key(4)]
    public ushort PubPort { get; set; }

    [IgnoreMember]   
    public string Id { get; set; }

    [IgnoreMember]
    public DateTime LastSeen { get; init; } = DateTime.UtcNow;
}