using Faster.MessageBus.Shared;

namespace Faster.MessageBus.Features.Discovery.Contracts;

/// <summary>
/// Provides methods to manage and track mesh nodes in a Local store.
/// </summary>
internal interface IMeshRepository
{
    /// <summary>
    /// Attempts to add a new mesh node to the store.
    /// </summary>
    /// <param name="info">The mesh node info to add.</param>
    /// <returns><c>true</c> if the node was added; <c>false</c> if it already exists.</returns>
    bool Add(MeshInfo info);

    /// <summary>
    /// Removes a mesh node from the store.
    /// </summary>
    /// <param name="info">The mesh node info to remove.</param>
    /// <returns><c>true</c> if the node was removed; otherwise, <c>false</c>.</returns>
    bool Remove(MeshInfo info);

    /// <summary>
    /// Updates an existing mesh node's information in the store.
    /// </summary>
    /// <param name="info">The updated mesh node info.</param>
    void Update(MeshInfo info);

    /// <summary>
    /// Returns all mesh nodes currently in the store.
    /// </summary>
    /// <returns>A list of all mesh node information.</returns>
    IList<MeshInfo> All();
}

