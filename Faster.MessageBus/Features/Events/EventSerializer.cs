using Faster.MessageBus.Features.Events.Contracts;
using MessagePack;
using MessagePack.Resolvers;
using System;

namespace Faster.MessageBus.Features.Events
{
    /// <summary>
    /// An internal implementation of <see cref="IEventSerializer"/> that uses the MessagePack library
    /// for high-performance serialization and deserialization of event objects.
    /// </summary>
    /// <remarks>
    /// This serializer is configured to use LZ4 block array compression to reduce the network payload size of events.
    /// </remarks>
    internal class EventSerializer : IEventSerializer
    {
        /// <summary>
        /// Holds the pre-configured MessagePack options, including LZ4 compression and the resolver.
        /// </summary>
        private readonly MessagePackSerializerOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventSerializer"/> class.
        /// </summary>
        /// <remarks>
        /// The constructor pre-configures the MessagePack serialization options for performance,
        /// enabling LZ4 compression and using the standard contract resolver.
        /// </remarks>
        public EventSerializer()
        {
            _options = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray)
                .WithResolver(TypelessContractlessStandardResolver.Instance);
        }

        /// <summary>
        /// Deserializes a byte array into an object of a specified runtime type.
        /// </summary>
        /// <param name="data">The byte array containing the MessagePack-serialized event data.</param>
        /// <param name="type">The target <see cref="Type"/> to deserialize the object into.</param>
        /// <returns>The deserialized event object, or null if the data is empty.</returns>
        public object? Deserialize(byte[] data, Type type)
        {
            return MessagePackSerializer.Deserialize(type, data, _options);
        }

        /// <summary>
        /// Deserializes a byte array into a strongly-typed event object.
        /// </summary>
        /// <typeparam name="T">The target type of the event.</typeparam>
        /// <param name="data">The byte array containing the MessagePack-serialized event data.</param>
        /// <returns>The deserialized event object of type <typeparamref name="T"/>.</returns>
        public T Deserialize<T>(byte[] data)
        {
            return MessagePackSerializer.Deserialize<T>(data, _options);
        }

        /// <summary>
        /// Serializes a strongly-typed event object into a byte array.
        /// </summary>
        /// <typeparam name="T">The type of the event object.</typeparam>
        /// <param name="obj">The event object to serialize.</param>
        /// <returns>A byte array containing the MessagePack-serialized event data.</returns>
        public byte[] Serialize<T>(T obj)
        {
            return MessagePackSerializer.Serialize(obj, _options);
        }
    }
}