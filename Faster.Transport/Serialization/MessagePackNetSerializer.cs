using MessagePack;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.Transport.Serialization;

/// <summary>
/// MessagePack-based serializer. Fast, compact, optional LZ4 compression.
/// </summary>
public sealed class MessagePackNetSerializer : ISerializer
{
    private readonly MessagePackSerializerOptions _options;

    /// <param name="options">
    /// Optional MessagePack options. If null, uses Contractless resolver (friendly for POCOs).
    /// </param>
    /// <param name="useLz4">Enable LZ4 block-array compression.</param>
    public MessagePackNetSerializer(MessagePackSerializerOptions? options = null, bool useLz4 = false)
    {
        var baseOptions = options ??
            MessagePackSerializerOptions.Standard
                .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);

        _options = baseOptions.WithCompression(
            useLz4 ? MessagePackCompression.Lz4BlockArray : MessagePackCompression.None);
    }

    public byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, _options);

    public T Deserialize<T>(ReadOnlySequence<byte> data) => MessagePackSerializer.Deserialize<T>(data, _options);

    public T Deserialize<T>(ReadOnlyMemory<byte> data) => MessagePackSerializer.Deserialize<T>(data, _options);

}