using MessagePack;
using System;
using System.Buffers;
using System.Text.Json;

namespace Faster.Transport.Serialization
{
    public interface ISerializer
    {
        byte[] Serialize<T>(T value);
    
        T Deserialize<T>(ReadOnlySequence<byte> data);
        T Deserialize<T>(ReadOnlyMemory<byte> data);
    }
}
