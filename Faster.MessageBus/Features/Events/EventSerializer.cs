using MessagePack;
using MessagePack.Resolvers;
using System;
using System.Buffers;

namespace Faster.MessageBus.Features.Events;

/// <summary>
/// Defines a contract for serializing and deserializing commands, optimized for low-allocation scenarios.
/// </summary>
public interface IEventSerializer
{
    /// <summary>
    /// Deserializes a sequence of bytes from a memory segment into an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize the object into.</typeparam>
    /// <param name="payload">The read-only memory segment containing the serialized data.</param>
    /// <returns>The deserialized object of type <typeparamref name="T"/>.</returns>
    T Deserialize<T>(in ReadOnlyMemory<byte> payload);

    /// <summary>
    /// Deserializes a byte array into an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize the object into.</typeparam>
    /// <param name="payload">The byte array containing the serialized data.</param>
    /// <returns>The deserialized object of type <typeparamref name="T"/>.</returns>
    T Deserialize<T>(byte[] payload);

    /// <summary>
    /// Deserializes a sequence of bytes into an object of type <typeparamref name="T"/>.
    /// This is the primary method for deserialization from potentially non-contiguous memory buffers.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize the object into.</typeparam>
    /// <param name="payload">The read-only sequence of bytes containing the serialized data.</param>
    /// <returns>The deserialized object of type <typeparamref name="T"/>.</returns>
    T Deserialize<T>(in ReadOnlySequence<byte> payload);

    /// <summary>
    /// Deserializes a sequence of bytes into an object of the specified type.
    /// </summary>
    /// <param name="payload">The read-only sequence of bytes containing the serialized data.</param>
    /// <param name="type">The target <see cref="Type"/> to deserialize the object into.</param>
    /// <returns>The deserialized object, which can be cast to the specified <paramref name="type"/>.</returns>
    /// <remarks>
    /// This method is optimized for single-segment <see cref="ReadOnlySequence{T}"/> instances.
    /// If the sequence is multi-segment, a copy to a contiguous array is required, which will cause a memory allocation.
    /// </remarks>
    object? Deserialize(in ReadOnlySequence<byte> payload, Type type);

    /// <summary>
    /// Serializes an object directly into a buffer provided by the writer.
    /// This is the primary, low-allocation method for serialization.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="obj">The object to serialize.</param>
    /// <param name="writer">The buffer writer to write the serialized data to.</param>
    /// <remarks>
    /// The caller is responsible for providing an <see cref="IBufferWriter{T}"/> implementation,
    /// typically one that uses <see cref="ArrayPool{T}"/> to avoid allocations.
    /// </remarks>
    void Serialize<T>(T obj, IBufferWriter<byte> writer);

    /// <summary>
    /// Serializes an object of a specified type directly into a buffer provided by the writer.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="obj">The object to serialize.</param>
    /// <param name="type">The runtime <see cref="Type"/> of the object to serialize.</param>
    /// <param name="writer">The buffer writer to write the serialized data to.</param>
    void Serialize<T>(T obj, Type type, IBufferWriter<byte> writer);

    /// <summary>
    /// Serializes an object to a new byte array.
    /// </summary>
    /// <remarks>
    /// This method causes a memory allocation and should be avoided in high-performance paths.
    /// </remarks>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>A new byte array containing the serialized data.</returns>
    byte[] Serialize<T>(T obj);
}

/// <summary>
/// Serializes and deserializes objects using MessagePack with LZ4 compression.
/// Implements the <see cref="ICommandSerializer"/> interface.
/// </summary>
public class CommandSerializer : IEventSerializer
{
    /// <summary>
    /// Holds the configured MessagePack _options, including LZ4 compression and the resolver.
    /// </summary>
    private readonly MessagePackSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandSerializer"/> class.
    /// It configures the default MessagePack _options to use LZ4 block array compression and the standard resolver.
    /// </summary>
    public CommandSerializer()
    {
        _options = MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray)
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
        // If the sequence is a single segment, we can do this without copying.
        if (payload.IsSingleSegment)
        {
            return MessagePackSerializer.Deserialize(type, payload.First, _options);
        }

        // If it's multi-segment, we must copy it to a contiguous array first.
        // This is a rare case but must be handled.
        var buffer = payload.ToArray();
        return MessagePackSerializer.Deserialize(type, buffer, _options);
    }
}