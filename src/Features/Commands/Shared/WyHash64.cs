using System;
using System.Collections.Generic;

namespace Faster.MessageBus.Features.Commands.Shared;

/// <summary>
/// Provides a fast, non-cryptographic 64-bit hash generator based on the WyHash algorithm.
/// This static class is used to generate pseudo-random numbers, for example, as correlation IDs.
/// <para>
/// For more information on the algorithm, see: https://github.com/wangyi-fudan/wyhash
/// </para>
/// </summary>
/// <remarks>
/// <strong style="color: red;">Warning:</strong> This static implementation is <strong style="color: red;">not thread-safe</strong>.
/// The internal <c>_seed</c> is a shared static variable that is read and modified in the <c>Next()</c> and <c>NextBytes()</c>
/// methods without any locking. Concurrent calls from multiple threads can lead to race conditions,
/// producing duplicate or incorrect values. If used in a multi-threaded context, access to this class must
/// be synchronized externally (e.g., using a <c>lock</c> statement).
/// </remarks>
public static class WyHash64
{
    /// <summary>
    /// The internal state of the pseudo-random number generator.
    /// </summary>
    private static ulong _seed;

    #region WyHash Magic Constants
    /// <summary>The first secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier0 = 0x243f6a8885a308d3;
    /// <summary>The second secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier1 = 0x13198a2e03707344;
    /// <summary>The third secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier2 = 0xa4093822299f31d0;
    /// <summary>The fourth secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier3 = 0x082efa98ec4e6c89;
    /// <summary>The fifth secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier4 = 0x452821e638d01377;
    /// <summary>The sixth secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier5 = 0xbe5466cf34e90c6c;
    /// <summary>The seventh secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier6 = 0xc0ac29b7c97c50dd;
    /// <summary>The eighth secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier7 = 0x3f84d5b5b5470917;
    /// <summary>The ninth secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier8 = 0x9216d5d98979fb1b;
    /// <summary>The tenth secret multiplier for the WyHash algorithm.</summary>
    public static readonly ulong Multiplier9 = 0xd1310ba698dfb5ac;
    #endregion

    /// <summary>
    /// A collection of the predefined secret multipliers used to initialize the seed.
    /// </summary>
    public static IList<ulong> Seeds = new[]
    {
        Multiplier0, Multiplier1, Multiplier2, Multiplier3, Multiplier4, Multiplier5, Multiplier6, Multiplier7, Multiplier8, Multiplier9
    };

    /// <summary>
    /// Initializes the static instance of the <see cref="WyHash64"/> class.
    /// It creates a reasonably unique starting seed for the generator based on the current system time.
    /// </summary>
    static WyHash64()
    {
        ulong ticks = (ulong)DateTime.UtcNow.Ticks;
        // Pick a seed from the list based on the last digit of the ticks count for variability.
        var seed = Seeds[(int)(ticks % 9)];
        _seed = ticks * seed;
    }

    /// <summary>
    /// Generates the next 64-bit pseudo-random unsigned integer in the sequence.
    /// </summary>
    /// <returns>A pseudo-random <see cref="ulong"/> value.</returns>
    public static ulong Next()
    {
        // Increment the seed to ensure the next hash is different.
        _seed += 0x60bee2bee120fc15UL;

        // Perform two rounds of WyHash mixing using 128-bit multiplication and XOR-folding.
        UInt128 tmp = (UInt128)_seed * 0xa3b195354a39b70dUL;
        ulong m1 = (ulong)(tmp >> 64) ^ (ulong)tmp;

        tmp = (UInt128)m1 * 0x1b03738712fad5c9UL;
        ulong m2 = (ulong)(tmp >> 64) ^ (ulong)tmp;

        return m2;
    }

    /// <summary>
    /// Generates the next pseudo-random number in the sequence and returns it as an 8-byte array.
    /// </summary>
    /// <returns>An 8-byte array containing a pseudo-random value.</returns>
    public static byte[] NextBytes()
    {
        _seed += 0x60bee2bee120fc15UL;

        UInt128 tmp = (UInt128)_seed * 0xa3b195354a39b70dUL;
        ulong m1 = (ulong)(tmp >> 64) ^ (ulong)tmp;

        tmp = (UInt128)m1 * 0x1b03738712fad5c9UL;
        ulong m2 = (ulong)(tmp >> 64) ^ (ulong)tmp;

        return BitConverter.GetBytes(m2);
    }
}