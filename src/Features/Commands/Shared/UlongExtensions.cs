using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Commands.Shared;

public static class FastConvert
{
    public static ulong BytesToUlong(byte[] buffer, int offset = 0)
    {
        return MemoryMarshal.Read<ulong>(buffer.AsSpan(offset));
    }

    public static void UlongToBytes(ulong value, byte[] buffer, int offset = 0)
    {
        MemoryMarshal.Write(buffer.AsSpan(offset), ref value);
    }
}

