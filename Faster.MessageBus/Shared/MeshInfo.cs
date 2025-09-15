using MessagePack;

namespace Faster.MessageBus.Shared;

[MessagePackObject]
public record MeshInfo(
    [property: Key(0)] string Id,
    [property: Key(1)] string Name,
    [property: Key(2)] string Address,
    [property: Key(3)] string ApplicationID,
    [property: Key(4)] ushort RpcPort,
    [property: Key(5)] ushort PubPort)
{
    [IgnoreMember]
    public DateTime LastSeen { get; init; } = DateTime.UtcNow;
}
