using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Commands.Shared;

using System.Collections.Concurrent;

/// <summary>
/// A high-performance, thread-safe object pool.
/// </summary>
/// <typeparam name="T">The type of object to pool.</typeparam>
public sealed class ConcurrentObjectPool<T> where T : class
{
    private readonly ConcurrentBag<T> _items = new ConcurrentBag<T>();
    private readonly Func<T> _factory;

    /// <summary>
    /// Gets the number of items currently available in the pool.
    /// This is a snapshot and may change immediately.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Initializes a new instance of the ConcurrentObjectPool.
    /// </summary>
    /// <param name="factory">The function used to create new objects when the pool is empty.</param>
    /// <param name="initialSize">The number of items to pre-populate the pool with.</param>
    public ConcurrentObjectPool(Func<T> factory, int initialSize = 0)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        for (int i = 0; i < initialSize; i++)
        {
            _items.Add(_factory());
        }
    }

    /// <summary>
    /// Retrieves an object from the pool or creates a new one using the factory.
    /// </summary>
    /// <returns>An instance of T.</returns>
    public T Get()
    {
        // TryTake is the lock-free way to get an item.
        if (_items.TryTake(out T item))
        {
            return item;
        }

        // Pool was empty, so create a new one.
        return _factory();
    }

    /// <summary>
    /// Returns an object to the pool.
    /// </summary>
    /// <param name="item">The object to return to the pool.</param>
    /// <param name="resetAction">An optional action to reset the object's state.</param>
    public void Return(T item, Action<T> resetAction = null)
    {    
      //  resetAction?.Invoke(item);
        _items.Add(item);
    }
}

