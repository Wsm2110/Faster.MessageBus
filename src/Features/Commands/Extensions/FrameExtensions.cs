using NetMQ;

namespace Faster.MessageBus.Features.Commands.Extensions;

public static class FrameExtensions
{
    public static IOutgoingSocket SendSpanFrame(this IOutgoingSocket socket, ReadOnlySpan<byte> span, bool more = false)
    {
        var msg = new Msg();
        msg.InitPool(span.Length);          // allocate NetMQ native buffer
        span.CopyTo(msg);                   // copy data into Msg
        socket.Send(ref msg, more);         // send the message
        msg.Close();

        return socket;// release native resources
    }

    public static void SendMemoryFrame(this IOutgoingSocket socket, ReadOnlyMemory<byte> memory, bool more = false)
    {
        socket.SendSpanFrame(memory.Span, more); // delegate to Span version
    }
}