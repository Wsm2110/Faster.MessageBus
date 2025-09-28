namespace Faster.MessageBus.Features.Commands.Contracts
{
    /// <summary>
    /// Defines the contract for a command routing filter.
    /// A routing filter is a probabilistic set membership structure (Bloom-style)
    /// that supports adding pre-hashed values and testing for possible existence.
    /// </summary>
    internal interface ICommandRoutingFilter
    {
        /// <summary>
        /// Initializes the filter with the expected number of items
        /// and the desired false positive rate.
        /// </summary>
        /// <param name="Length">Expected number of items to insert.</param>
        /// <param name="falsePositiveRate">
        /// Desired false positive probability (0 < falsePositiveRate < 1).
        /// Lower values require more memory.
        /// </param>
        void Initialize(int Length, double falsePositiveRate = 0.01);

        /// <summary>
        /// Checks whether the filter might contain the given precomputed hash.
        /// Returns <c>true</c> if the element might be present, <c>false</c> if definitely not.
        /// </summary>
        /// <param name="preHash">The precomputed 64-bit hash of the element.</param>
        /// <returns>
        /// <c>true</c> if the element may be present (with some false positive probability),
        /// <c>false</c> if the element is definitely not present.
        /// </returns>
        bool MightContain(ulong preHash);

        /// <summary>
        /// Adds the given precomputed hash to the filter.
        /// This operation is idempotent; adding the same element multiple times has no effect.
        /// </summary>
        /// <param name="preHash">The precomputed 64-bit hash of the element.</param>
        void Add(ulong preHash);
    }
}
