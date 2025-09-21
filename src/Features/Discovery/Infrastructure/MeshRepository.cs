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
        private readonly Dictionary<ulong, MeshInfo> _discovered = new();

        /// <inheritdoc/>
        public bool Add(MeshInfo info) => _discovered.TryAdd(info.MeshId, info);

        /// <inheritdoc/>
        public bool Remove(MeshInfo info) => _discovered.Remove(info.MeshId);

        /// <inheritdoc/>
        public void Update(MeshInfo info) => _discovered[info.MeshId] = info;

        /// <inheritdoc/>
        public IList<MeshInfo> All() => _discovered.Values.ToList();
    }
}
