namespace Faster.MessageBus.Shared;

/// <summary>
/// Event representing that a mesh node has joined the network.
/// </summary>
/// <param name="Info">Information about the joined mesh node.</param>
public record MeshJoined(MeshInfo Info);


/// <summary>
/// Event representing that a mesh node has left the network.
/// </summary>
/// <param name="Info">Information about the departed mesh node.</param>
public record MeshRemoved(MeshInfo Info);
