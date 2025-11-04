using Faster.MessageBus.Features.Commands.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices; // used by targetframework >= 7.0

namespace Faster.MessageBus.Features.Commands
{
    /// <summary>
    /// Implements a high-performance, lightweight Bloom filter for routing commands across mesh nodes.
    /// Uses a fixed k=2 hash scheme: splits a 64-bit pre-hash into two 32-bit hashes and sets/checks bits in a byte array.
    /// Optimized for fast, zero-allocation bit operations using <see cref="Unsafe"/> or unsafe code.
    /// </summary>
    public class CommandRoutingFilter : ICommandRoutingFilter
    {
        private const int POS_MASK = 7; // mask for modulo 8, as each byte has 8 bits
        private byte[]? _bits;          // underlying byte array storing bit flags
        private int _bitCount;           // total number of bits in the filter

        /// <summary>
        /// Initializes the filter with a target capacity and optional false positive rate.
        /// </summary>
        /// <param name="length">Expected number of items to store in the filter.</param>
        /// <param name="falsePositiveRate">Acceptable false positive probability (default 0.01).</param>
        public void Initialize(int length, double falsePositiveRate = 0.01)
        {
            if (length == 0)
            {
                length = 2; // minimum size to avoid empty array
            }

            int perfectSize = Calculate(length, falsePositiveRate);
            _bitCount = NextPow2(perfectSize); // round up to next power of two for fast masking
            _bits = new byte[(_bitCount + 7) >> 3]; // allocate bytes
        }

        /// <summary>
        /// Returns the internal byte array representing the membership table.
        /// Can be used for external serialization or sharing with other processes.
        /// </summary>
        /// <returns>The byte array containing the Bloom filter bits.</returns>
        public byte[] GetMembershipTable()
        {
            return _bits!;
        }

        /// <summary>
        /// Adds an element to the filter by setting two bits derived from splitting the 64-bit hash.
        /// </summary>
        /// <param name="preHash">A 64-bit pre-computed hash of the item.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(ulong preHash)
        {
            uint h1 = (uint)preHash;
            uint h2 = (uint)(preHash >> 32);

            SetBit(h1);
            SetBit(h2);
        }

        /// <summary>
        /// Checks whether an element might exist in the filter.
        /// Both bits derived from splitting the 64-bit hash must be set.
        /// </summary>
        /// <param name="preHash">A 64-bit pre-computed hash of the item.</param>
        /// <returns>True if the element might be present, false if definitely absent.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MightContain(ulong preHash)
        {
            uint h1 = (uint)preHash;
            uint h2 = (uint)(preHash >> 32);

            return IsBitSet(h1) && IsBitSet(h2);
        }

        /// <summary>
        /// Sets a single bit corresponding to the given hash in the underlying byte array.
        /// </summary>
        /// <param name="hash">The 32-bit hash used to compute the bit position.</param>
        /// <returns>True if the bit was previously 0, false if already set.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SetBit(uint hash)
        {
            nuint byteIndex = (nuint)(hash & _bits.Length - 1);
            int bitOffset = (int)hash & POS_MASK;
            byte mask = (byte)(1 << bitOffset);
            unsafe
            {
                fixed (byte* ptr = _bits)
                {
                    byte* p = ptr + byteIndex;
                    byte old = *p;
                    *p = (byte)(old | mask);
                    return (old & mask) == 0;
                }
            }
        }

        /// <summary>
        /// Tests whether a single bit corresponding to the given hash is set in the byte array.
        /// </summary>
        /// <param name="hash">The 32-bit hash used to compute the bit position.</param>
        /// <returns>True if the bit is set, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsBitSet(uint hash)
        {
            nuint byteIndex = (nuint)(hash & _bits.Length - 1);
            int bitOffset = (int)hash & POS_MASK;
            byte mask = (byte)(1 << bitOffset);

            unsafe
            {
                fixed (byte* ptr = _bits)
                {
                    byte* p = ptr + byteIndex;
                    return (*p & mask) != 0;
                }
            }
        }

        /// <summary>
        /// Calculates the number of bits needed for a given number of expected items and false positive rate.
        /// This formula is derived from the Bloom filter equation for k=2 hashes.
        /// </summary>
        /// <param name="expected">Number of expected items.</param>
        /// <param name="falsePositiveRate">Target false positive probability.</param>
        /// <returns>The number of bits to allocate.</returns>
        private int Calculate(int expected, double falsePositiveRate)
        {
            double m = -(expected * Math.Log(falsePositiveRate)) / (Math.Pow(Math.Log(2), 2));
            return (int)Math.Ceiling(m);
        }

        /// <summary>
        /// Rounds a value up to the next power of two. Used for fast modulo masking.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPow2(int c)
        {
            c--;
            c |= c >> 1;
            c |= c >> 2;
            c |= c >> 4;
            c |= c >> 8;
            c |= c >> 16;
            return c + 1;
        }

        #region Static Helpers

        /// <summary>
        /// Checks if a 64-bit hash might exist in a Bloom filter represented by a raw byte array.
        /// </summary>
        /// <param name="bits">Byte array representing the filter.</param>
        /// <param name="preHash">64-bit hash to test.</param>
        /// <returns>True if the hash might be present, false if definitely absent.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryContains(byte[] bits, ulong preHash)
        {
            uint h1 = (uint)preHash;
            uint h2 = (uint)(preHash >> 32);

            return IsBitSet(bits, h1) && IsBitSet(bits, h2);
        }

        /// <summary>
        /// Helper to test a single bit in a raw byte array.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBitSet(byte[] bits, uint hash)
        {
            nuint byteIndex = (nuint)(hash & bits.Length - 1);
            int bitOffset = (int)hash & POS_MASK;
            byte mask = (byte)(1 << bitOffset);

            unsafe
            {
                fixed (byte* ptr = bits)
                {
                    byte* p = ptr + byteIndex;
                    return (*p & mask) != 0;
                }
            }
        }

        #endregion
    }
}
