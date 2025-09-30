using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Shared;
using System.Collections.Concurrent;


namespace Faster.MessageBus.Features.Discovery.Infrastructure
{
    /// <summary>
    /// In-memory implementation of <see cref="IMeshRepository"/> using a dictionary keyed by mesh MeshId.
    /// </summary>
    internal class MeshRepository : IMeshRepository
    {
        // Internal dictionary storing discovered mesh nodes, keyed by their MeshId
        private readonly ConcurrentDictionary<ulong, MeshContext> _discovered = new();

        /// <inheritdoc/>
        public bool TryAdd(MeshContext info)
        {
            if (_discovered.ContainsKey(info.MeshId))
                return false; // already exists

            _discovered.TryAdd(info.MeshId, info);
            return true; // added successfully
        }

        /// <inheritdoc/>
        public bool TryRemove(MeshContext info) => _discovered.TryRemove(info.MeshId, out _);

        /// <inheritdoc/>
        public void Update(MeshContext info) => _discovered[info.MeshId] = info;

        /// <inheritdoc/>
        public IList<MeshContext> All() => _discovered.Values.ToList();
    }
}
