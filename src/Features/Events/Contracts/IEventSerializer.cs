using System.Buffers;

/// <summary>
/// Defines a contract for serializing and deserializing events, optimized for high-performance, low-allocation scenarios.
/// </summary>
public interface IEventSerializer
{
    /// <summary>
    /// Deserializes a contiguous memory region into an object of type <typeparamref name="T"/>.
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
    /// Deserializes a sequence of bytes into an object of a specified runtime type.
    /// </summary>
    /// <param name="payload">The read-only sequence of bytes containing the serialized data.</param>
    /// <param name="type">The target <see cref="Type"/> to deserialize the object into.</param>
    /// <returns>The deserialized object, which must be cast to the specified <paramref name="type"/>.</returns>
    /// <remarks>
    /// This method is optimized for single-segment <see cref="ReadOnlySequence{T}"/> instances.
    /// If the sequence is multi-segment, it must be copied to a contiguous array, which will cause a memory allocation.
    /// </remarks>
    object? Deserialize(in ReadOnlySequence<byte> payload, Type type);

    /// <summary>
    /// Serializes an object directly into a buffer writer.
    /// This is the primary, low-allocation method for serialization.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="obj">The object instance to serialize.</param>
    /// <param name="writer">The buffer writer to write the serialized data into.</param>
    /// <remarks>
    /// The caller is responsible for providing an <see cref="IBufferWriter{T}"/> implementation,
    /// such as <c>CommunityToolkit.HighPerformance.Buffers.ArrayPoolBufferWriter&lt;byte&gt;</c>, to avoid allocations.
    /// </remarks>
    void Serialize<T>(T obj, IBufferWriter<byte> writer);

    /// <summary>
    /// Serializes an object of a specified runtime type directly into a buffer writer.
    /// </summary>
    /// <typeparam name="T">The base type of the object to serialize (e.g., <see cref="object"/> or an interface).</typeparam>
    /// <param name="obj">The object instance to serialize.</param>
    /// <param name="type">The specific runtime <see cref="Type"/> of the object to serialize.</param>
    /// <param name="writer">The buffer writer to write the serialized data into.</param>
    void Serialize<T>(T obj, Type type, IBufferWriter<byte> writer);

    /// <summary>
    /// Serializes an object to a new byte array.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>A new byte array containing the serialized data.</returns>
    /// <remarks>
    /// This method always causes a memory allocation for the returned array and should be avoided in high-performance paths.
    /// Prefer the overload that accepts an <see cref="IBufferWriter{T}"/>.
    /// </remarks>
    byte[] Serialize<T>(T obj);
}