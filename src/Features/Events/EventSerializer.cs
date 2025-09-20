using MessagePack;
using MessagePack.Resolvers;
using System;
using System.Buffers;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Implements <see cref="IEventSerializer"/> using the MessagePack library.
/// This implementation is configured for high performance, using LZ4 compression
/// and a typeless resolver to handle serialization of objects via their interface or base type.
/// </summary>
public class EventSerializer : IEventSerializer
{
    /// <summary>
    /// Holds the configured MessagePack options, including LZ4 compression and the resolver.
    /// This options object is reused for all serialization and deserialization calls to ensure consistency.
    /// </summary>
    private readonly MessagePackSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSerializer"/> class.
    /// It configures the default MessagePack options for the application.
    /// </summary>
    public EventSerializer()
    {
        // Configure the serializer options.
        _options = MessagePackSerializerOptions.Standard
            // Use LZ4 compression to reduce the size of the payload over the network.
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            // Use a typeless resolver to embed type information in the payload.
            // This is crucial for deserializing objects when only a base class or interface (like IEvent or ICommand) is known.
            .WithResolver(TypelessContractlessStandardResolver.Instance);
    }

    /// <inheritdoc/>
    public void Serialize<T>(T obj, IBufferWriter<byte> writer)
    {
        MessagePackSerializer.Serialize(writer, obj, _options);
    }

    /// <inheritdoc/>
    public void Serialize<T>(T obj, Type type, IBufferWriter<byte> writer)
    {
        MessagePackSerializer.Serialize(type, writer, obj, _options);
    }

    /// <inheritdoc/>
    [Obsolete("This method allocates a new byte array. Use the Serialize(T obj, IBufferWriter<byte> writer) overload for performance-critical code.")]
    public byte[] Serialize<T>(T obj)
    {
        return MessagePackSerializer.Serialize(obj, _options);
    }

    /// <inheritdoc/>
    public T Deserialize<T>(byte[] payload)
    {
        return MessagePackSerializer.Deserialize<T>(payload, _options);
    }

    /// <inheritdoc/>
    public T Deserialize<T>(in ReadOnlySequence<byte> payload)
    {
        return MessagePackSerializer.Deserialize<T>(payload, _options);
    }

    /// <inheritdoc/>
    public T Deserialize<T>(in ReadOnlyMemory<byte> payload)
    {
        return MessagePackSerializer.Deserialize<T>(payload, _options);
    }

    /// <inheritdoc/>
    public object? Deserialize(in ReadOnlySequence<byte> payload, Type type)
    {
        // MessagePack doesn't have a direct (Type, ReadOnlySequence) overload.
        // We must handle the two possible structures of a ReadOnlySequence.

        // Fast path: If the sequence is composed of a single, contiguous block of memory,
        // we can deserialize it directly without any copying or allocation. This is the common case.
        if (payload.IsSingleSegment)
        {
            return MessagePackSerializer.Deserialize(type, payload.First, _options);
        }

        // Slow path: If the sequence is fragmented across multiple memory blocks (multi-segment),
        // we must first copy it to a single, contiguous array before deserializing.
        // This is a rare case but is required for correctness.
        var buffer = payload.ToArray();
        return MessagePackSerializer.Deserialize(type, buffer, _options);
    }
}