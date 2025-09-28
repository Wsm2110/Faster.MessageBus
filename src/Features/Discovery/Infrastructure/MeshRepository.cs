using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Shared;


namespace Faster.MessageBus.Features.Discovery.Infrastructure
{
    /// <summary>
    /// In-memory implementation of <see cref="IMeshRepository"/> using a dictionary keyed by mesh MeshId.
    /// </summary>
    internal class MeshRepository : IMeshRepository
    {
        // Internal dictionary storing discovered mesh nodes, keyed by their MeshId
        private readonly Dictionary<ulong, MeshContext> _discovered = new();

        /// <inheritdoc/>
        public bool Add(MeshContext info)
        {
            if (_discovered.ContainsKey(info.MeshId))
                return false; // already exists

            _discovered.Add(info.MeshId, info);
            return true; // added successfully
        }

        /// <inheritdoc/>
        public bool Remove(MeshContext info) => _discovered.Remove(info.MeshId);

        /// <inheritdoc/>
        public void Update(MeshContext info) => _discovered[info.MeshId] = info;

        /// <inheritdoc/>
        public IList<MeshContext> All() => _discovered.Values.ToList();
    }
}
