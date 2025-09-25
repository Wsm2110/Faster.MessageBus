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
        int pid = System.Diagnostics.Process.GetCurrentProcess().Id; // instead of Environment.ProcessId
        int count = Interlocked.Increment(ref _counter);

        byte[] buffer = new byte[8];
        Array.Copy(BitConverter.GetBytes(pid), 0, buffer, 0, 4);
        Array.Copy(BitConverter.GetBytes(count), 0, buffer, 4, 4);

        return buffer;
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