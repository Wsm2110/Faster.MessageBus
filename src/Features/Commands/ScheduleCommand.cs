﻿using CommunityToolkit.HighPerformance.Buffers;
using NetMQ;
using NetMQ.Sockets;
using System.Runtime.InteropServices;

namespace Faster.MessageBus.Features.Commands;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct ScheduleCommand(DealerSocket Socket, ulong Topic, ulong CorrelationId, ReadOnlyMemory<byte> Payload);


