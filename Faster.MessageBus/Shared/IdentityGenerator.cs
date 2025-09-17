namespace Faster.MessageBus.Shared;

using System;
using System.Text;
using System.Threading;

/// <summary>
/// Generates compact, globally unique DealerSocket identities (8 bytes).
/// Combines process ID with a monotonically increasing counter.
/// </summary>
public static class DealerIdentityGenerator
{
    private static int _counter = Environment.TickCount;

    /// <summary>
    /// Creates a new unique 8-byte identity for use with DealerSocket.
    /// </summary>
    public static byte[] Create()
    {
        var pid = Environment.ProcessId;                       // 4 bytes
        var count = Interlocked.Increment(ref _counter);       // 4 bytes

        Span<byte> buffer = stackalloc byte[8];
        BitConverter.TryWriteBytes(buffer[..4], pid);
        BitConverter.TryWriteBytes(buffer[4..], count);

        return buffer.ToArray();
    }

    /// <summary>
    /// Same as <see cref="Create"/>, but returned as an ASCII-safe string (Base32 encoded).
    /// Length: ~13 characters.
    /// </summary>
    public static string CreateString()
    {
        return Convert.ToBase64String(Create()); // compact ASCII form
    }

}