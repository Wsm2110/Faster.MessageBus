using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Shared;


namespace Faster.MessageBus.Features.Discovery.Infrastructure
{
    /// <summary>
    /// In-memory implementation of <see cref="IMeshRepository"/> using a dictionary keyed by mesh Id.
    /// </summary>
    internal class MeshRepository : IMeshRepository
    {
        // Internal dictionary storing discovered mesh nodes, keyed by their Id
        private readonly Dictionary<string, MeshInfo> _discovered = new();

        /// <inheritdoc/>
        public bool Add(MeshInfo info) => _discovered.TryAdd(info.Id, info);

        /// <inheritdoc/>
        public bool Remove(MeshInfo info) => _discovered.Remove(info.Id);

        /// <inheritdoc/>
        public void Update(MeshInfo info) => _discovered[info.Id] = info;

        /// <inheritdoc/>
        public IList<MeshInfo> All() => _discovered.Values.ToList();
    }
}
