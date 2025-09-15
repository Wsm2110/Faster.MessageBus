using NetMQ;
using System.Runtime.InteropServices;

namespace Faster.MessageBus.Features.Commands;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct ScheduleCommand(NetMQSocket Socket, ulong Topic, ulong CorrelationId, ReadOnlyMemory<byte> Payload);


