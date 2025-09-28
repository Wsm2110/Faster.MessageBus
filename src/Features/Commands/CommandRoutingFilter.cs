using Faster.MessageBus.Features.Commands.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices; // dont remove used by targetframework >= 7.0

namespace Faster.MessageBus.Features.Commands
{
    internal class CommandRoutingFilter : ICommandRoutingFilter
    {
        private const int POS_MASK = 7; // for modulo 8
        private byte[]? _bits;
        private int _bitCount;

        /// <summary>
        /// Initializes the filter with the expected number of items.
        /// Each byte represents 8 bits.
        /// </summary>
        public void Initialize(int length, double falsePositiveRate = 0.01)
        {
            int perfectSize = Calculate(length, falsePositiveRate);
            _bitCount = NextPow2(perfectSize); // round up for fast masking
            _bits = new byte[(_bitCount + 7) >> 3];
        }

        /// <summary>
        /// Adds an element by splitting the 64-bit hash into two 32-bit hashes,
        /// and setting two corresponding bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(ulong preHash)
        {
            uint h1 = (uint)preHash;
            uint h2 = (uint)(preHash >> 32);

            SetBit(h1);
            SetBit(h2);
        }

        /// <summary>
        /// Checks if both bits (from the two hash splits) are set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MightContain(ulong preHash)
        {
            uint h1 = (uint)preHash;
            uint h2 = (uint)(preHash >> 32);

            return IsBitSet(h1) && IsBitSet(h2);
        }

        /// <summary>
        /// Helper: set a bit for a given hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SetBit(uint hash)
        {
            // ⚡ use bitmask instead of modulus (since _bitCount is power of two)
            int bitIndex = (int)(hash & (_bitCount - 1));
            nuint byteIndex = (nuint)(bitIndex >> 3);
            int bitOffset = bitIndex & POS_MASK;
            byte mask = (byte)(1 << bitOffset);

#if NET48
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
#else
            ref byte b = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_bits), byteIndex);
            byte old = b;
            b = (byte)(old | mask);
            return (old & mask) == 0;
#endif
        }

        /// <summary>
        /// Helper: test if a bit is set for a given hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsBitSet(uint hash)
        {
            int bitIndex = (int)(hash & (_bitCount - 1)); // ⚡ mask instead of modulus
            nuint byteIndex = (nuint)(bitIndex >> 3);
            int bitOffset = bitIndex & POS_MASK;
            byte mask = (byte)(1 << bitOffset);

#if NET48
            unsafe
            {
                fixed (byte* ptr = _bits)
                {
                    byte* p = ptr + byteIndex;
                    return (*p & mask) != 0;
                }
            }
#else
            ref byte b = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_bits), byteIndex);
            return (b & mask) != 0;
#endif
        }

        /// <summary>
        /// Calculates the required number of bits (m) for a given expected number of items and FPR.
        /// For this implementation k=2 is fixed (splitting ulong into 2 hashes).
        /// </summary>
        public int Calculate(int expected, double falsePositiveRate)
        {
            double m = -(expected * Math.Log(falsePositiveRate)) / (Math.Pow(Math.Log(2), 2));
            return (int)Math.Ceiling(m);
        }

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
    }
}
