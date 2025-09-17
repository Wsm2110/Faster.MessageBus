using NetMQ.Sockets;
using System.Runtime.InteropServices;

namespace Faster.MessageBus.Features.Events.Shared;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct ScheduleEvent(PublisherSocket Socket, ulong Topic, ReadOnlyMemory<byte> Payload);


