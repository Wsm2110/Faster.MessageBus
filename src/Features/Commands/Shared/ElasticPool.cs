using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;


namespace Faster.MessageBus.Features.Commands.Shared;

/// <summary>
/// A zero-allocation, high-performance, elastic object pool for <see cref="PendingReply{TResult}"/>. This implementation uses
/// <see cref="System.Collections.Concurrent.ConcurrentBag{T}"/> as its core storage.
///
/// Optimizations for this version:
/// 1. Zero-Allocation Hot Path: Rent() and Return() do not allocate managed memory on their fast paths.
/// 2. Fast, Contention-Free Lookups: ConcurrentBag provides thread-local storage, making lookups
///    and returns almost as fast as a non-thread-safe collection, while automatically handling
///    work-stealing when a thread's local cache is empty. This eliminates the need for a separate
///    global pool for the fast path.
/// 3. Simplified Logic: The entire local/global pool management is replaced by a single ConcurrentBag,
///    reducing code complexity.
/// 4. Dedicated Background Trimming: A background timer continues to handle the trimming of excess
///    objects created during bursts, keeping that work entirely off the hot path.
/// 5. False-Sharing Prevention: High-contention fields remain padded to prevent CPU cache line interference.
/// </summary>
public sealed class ElasticPool : IDisposable
{
    #region Fields

    /// <summary>
    /// The core thread-safe collection for storing idle pool items.
    /// </summary>
    private readonly ConcurrentBag<PendingReply<byte[]>> _items = new();

    /// <summary>
    /// The background timer responsible for periodically trimming excess objects from the pool.
    /// </summary>
    private readonly System.Threading.Timer _trimmer;

    /// <summary>
    // A flag to prevent redundant disposal of the pool.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// A padded integer tracking the total number of objects ever allocated by the pool (both idle and in-use).
    /// Padded to prevent false sharing with other concurrently accessed fields.
    /// </summary>
    private PaddedInt _allocated;

    /// <summary>
    /// A padded long storing the Environment.TickCount64 of the last time an object was allocated above MaxSize.
    /// Used by the trimmer to decide when a burst period has ended.
    /// </summary>
    private PaddedLong _lastBurstTicks;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the target number of objects to keep in the pool during pre-warming and trimming.
    /// </summary>
    public int CoreSize { get; }

    /// <summary>
    /// Gets the normal maximum number of objects allowed in the pool. The pool will trim back down to this size after a burst.
    /// </summary>
    public int MaxSize { get; }

    /// <summary>
    /// Gets the absolute maximum number of objects that can be allocated. The pool can temporarily grow to this size during a burst of activity.
    /// </summary>
    public int BurstMax { get; }

    /// <summary>
    /// Gets the time-to-live for excess objects created during a burst. After this period of inactivity, the trimmer will remove them.
    /// </summary>
    public TimeSpan BurstExcessTtl { get; }

    /// <summary>
    /// Gets the total number of live instances created by the pool (both rented and idle).
    /// </summary>
    /// <value>The total number of allocated objects.</value>
    public int Allocated => Volatile.Read(ref _allocated.Value);

    /// <summary>
    /// Gets the approximate number of idle instances currently available in the pool.
    /// </summary>
    /// <value>The number of objects currently available to be rented.</value>
    /// <remarks>
    /// Accessing <see cref="ConcurrentBag{T}.Count"/> can be slow as it needs to tally all thread-local queues.
    /// Use this property sparingly in performance-critical code.
    /// </remarks>
    public int IdleCount => _items.Count;

    #endregion

    #region Constructor and Disposal

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticPool"/> class with the specified configuration.
    /// </summary>
    /// <param name="coreSize">The target number of objects to pre-allocate and maintain.</param>
    /// <param name="maxSize">The normal maximum size of the pool.</param>
    /// <param name="burstMax">The absolute maximum size the pool can temporarily grow to during a burst.</param>
    /// <param name="burstExcessTtl">The time-to-live for excess objects created during a burst.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if size parameters are invalid.</exception>
    public ElasticPool(
        int coreSize = 0,
        int maxSize = 1024,
        int burstMax = 4096,
        TimeSpan? burstExcessTtl = null)
    {
        if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
        if (burstMax < maxSize) throw new ArgumentOutOfRangeException(nameof(burstMax), "burstMax must be >= maxSize");
        if (coreSize < 0 || coreSize > maxSize) throw new ArgumentOutOfRangeException(nameof(coreSize));

        CoreSize = coreSize;
        MaxSize = maxSize;
        BurstMax = burstMax;
        BurstExcessTtl = burstExcessTtl ?? TimeSpan.FromSeconds(15);

        if (CoreSize > 0)
        {
            Prewarm(CoreSize);
        }

        // The timer moves expensive trimming work off the hot path.
        var trimInterval = BurstExcessTtl;
        if (trimInterval < TimeSpan.FromMilliseconds(250))
        {
            trimInterval = TimeSpan.FromMilliseconds(250);
        }

        _trimmer = new System.Threading.Timer(TrimExcess, null, trimInterval, trimInterval);
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="ElasticPool"/> class.
    /// </summary>
    ~ElasticPool()
    {
        Dispose(false);
    }

    /// <summary>
    /// Disposes the pool, stopping the trimmer and clearing all items.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs the actual disposal of managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from <see cref="Dispose()"/>; false if called from the finalizer.</param>
    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _trimmer.Dispose();
       
            while (_items.TryTake(out var _))
            {
                // discard
            }
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Pre-allocates a specified number of instances and adds them to the pool, up to the <see cref="MaxSize"/> limit.
    /// </summary>
    /// <param name="count">The number of instances to create.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the pool has been disposed.</exception>
    public void Prewarm(int count)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
        for (int i = 0; i < count; i++)
        {
            if (Interlocked.Increment(ref _allocated.Value) > MaxSize)
            {
                Interlocked.Decrement(ref _allocated.Value);
                break;
            }
            _items.Add(new PendingReply<byte[]>());
        }
    }

    /// <summary>
    /// Rents an instance from the pool.
    /// </summary>
    /// <returns>A ready-to-use <see cref="PendingReply{TResult}"/> instance.</returns>
    /// <remarks>
    /// This method follows a multi-stage logic:
    /// 1. Fast Path: Tries to take an item from the <see cref="ConcurrentBag{T}"/>, which is a zero-allocation operation if an item is available locally.
    /// 2. Slow Path: If the pool is empty, it allocates a new item, provided the total allocated count is within <see cref="BurstMax"/>.
    /// 3. Contention Path: If the pool is empty and over the burst limit, it spin-waits for another thread to return an item.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown if the pool has been disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PendingReply<byte[]> Rent()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);

        // 1) Fast path: Try to get an object from the bag.
        if (_items.TryTake(out var item))
        {
            return item;
        }

        // 2) Slow path: Allocate a new instance if within burst limits.
        int allocatedCount = Interlocked.Increment(ref _allocated.Value);
        if (allocatedCount <= BurstMax)
        {
            if (allocatedCount > MaxSize)
            {
                Volatile.Write(ref _lastBurstTicks.Value, Environment.TickCount);
            }
            return new PendingReply<byte[]>();
        }

        // 3) Contention path: Over hard cap. Roll back allocation and spin-wait.
        Interlocked.Decrement(ref _allocated.Value);
        return WaitForObject();
    }

    /// <summary>
    /// Returns an instance to the pool, resetting its state to make it available for reuse.
    /// </summary>
    /// <param name="item">The item to return to the pool.</param>
    /// <remarks>
    /// This is an extremely fast, zero-allocation operation that pushes the item into the current thread's local queue within the <see cref="ConcurrentBag{T}"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(PendingReply<byte[]> item)
    {
        item.Reset();
        _items.Add(item);
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Handles the contention path when the pool is empty and has hit its <see cref="BurstMax"/> limit.
    /// It spin-waits efficiently until an item is returned to the pool by another thread.
    /// </summary>
    /// <returns>A <see cref="PendingReply{TResult}"/> instance once one becomes available.</returns>
    private PendingReply<byte[]> WaitForObject()
    {
        var spinner = new SpinWait();
        while (true)
        {
            if (_items.TryTake(out var item))
            {
                return item;
            }
            spinner.SpinOnce();
        }
    }

    /// <summary>
    /// The callback method for the background trimmer timer. It trims excess objects created during a burst.
    /// </summary>
    /// <param name="state">The state object (not used).</param>
    private void TrimExcess(object? state)
    {
        if (_disposed) return;

        int currentAllocated = Volatile.Read(ref _allocated.Value);
        if (currentAllocated <= MaxSize) return;

        // Check if the burst TTL has expired since the last burst allocation.
        long lastBurst = Volatile.Read(ref _lastBurstTicks.Value);
        if (lastBurst == 0 || unchecked(Environment.TickCount - lastBurst) < BurstExcessTtl.TotalMilliseconds)
        {
            return;
        }

        // Attempt to trim the pool down to MaxSize by removing idle objects.
        int trimGoal = currentAllocated - MaxSize;
        for (int i = 0; i < trimGoal; i++)
        {
            if (Volatile.Read(ref _allocated.Value) <= MaxSize) break;

            if (_items.TryTake(out var victim))
            {
                // By taking the item and letting it go out of scope, we allow the GC to collect it.
                Interlocked.Decrement(ref _allocated.Value);
            }
            else
            {
                // The bag is empty, can't trim more now.
                break;
            }
        }
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// A struct that pads an integer to a 128-byte size. This prevents "false sharing"
    /// of CPU cache lines between concurrently accessed fields on multi-core processors.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedInt
    {
        /// <summary>
        /// The integer value, offset to sit in its own cache line.
        /// </summary>
        [FieldOffset(64)]
        public int Value;
    }

    /// <summary>
    /// A struct that pads a long to a 128-byte size. This prevents "false sharing"
    /// of CPU cache lines between concurrently accessed fields on multi-core processors.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedLong
    {
        /// <summary>
        /// The long value, offset to sit in its own cache line.
        /// </summary>
        [FieldOffset(64)]
        public long Value;
    }

    #endregion
}


/// <summary>
/// A specialized, thread-safe object pool for PendingReply<byte[]>
/// implemented with a ConcurrentStack (LIFO).
/// </summary>
public sealed class PendingReplyPool
{
    private readonly ConcurrentStack<PendingReply<byte[]>> _items = new ConcurrentStack<PendingReply<byte[]>>();

    /// <summary>
    /// The factory to create new instances when the pool is empty.
    /// </summary>
    private readonly Func<PendingReply<byte[]>> _factory = () => new PendingReply<byte[]>();

    /// <summary>
    /// Gets the number of items currently available in the pool.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Initializes a new instance of the pool.
    /// </summary>
    /// <param name="initialSize">The number of items to pre-populate the pool with.</param>
    public PendingReplyPool(int initialSize = 0)
    {
        for (int i = 0; i < initialSize; i++)
        {
            _items.Push(new PendingReply<byte[]>());
        }
    }

    /// <summary>
    /// Retrieves a PendingReply<byte[]> from the pool or creates a new one.
    /// </summary>
    public PendingReply<byte[]> Get()
    {
        if (_items.TryPop(out PendingReply<byte[]> item))
        {
            return item;
        }
        return new PendingReply<byte[]>();
    }

    /// <summary>
    /// Resets and returns a PendingReply<byte[]> to the pool.
    /// </summary>
    public void Return(PendingReply<byte[]> item)
    {     
        // CRITICAL: Reset the object's state before returning it to the pool.
        // This makes it clean and ready for the next user.
        item.Reset();
        _items.Push(item);
    }
}