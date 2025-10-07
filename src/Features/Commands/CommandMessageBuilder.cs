namespace Faster.MessageBus.Features.Commands;

using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using System;
using System.Buffers;

public static class MessageBuilder
{
    public static ReadOnlyMemory<byte> BuildMessage<T>(
        ulong topic,
        Guid correlationId,
        T command,    
        ICommandSerializer serializer,
        ArrayPoolBufferWriter<byte> writer)
    {

        writer.Write(topic);

        writer.Write(correlationId);

        serializer.Serialize(command, writer);

        // Return the written memory (directly from pooled buffers)
        return writer.WrittenMemory;
    }
}