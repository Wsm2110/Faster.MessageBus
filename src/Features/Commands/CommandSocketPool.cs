using NetMQ.Sockets;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// High-performance pool of DealerSockets for a mesh context / dispatcher cluster.
/// Supports single-socket hot path, round-robin, hash-based, and least-loaded dispatch.
/// </summary>
internal class CommandSocketPool
{
    private readonly DealerSocket[] _sockets;
    private readonly int _count;
    private readonly bool _isPowerOfTwo;
    private readonly int[] _loadCounters; // For least-loaded strategy
    private int _currentIndex = -1;

    /// <summary>
    /// Initializes the pool with the provided DealerSockets.
    /// If possible, use power-of-two count for maximum round-robin efficiency.
    /// </summary>
    /// <param name="sockets">Array of initialized DealerSockets.</param>
    public CommandSocketPool(DealerSocket[] sockets)
    {      
        _sockets = sockets;
        _count = sockets.Length;
        _isPowerOfTwo = (_count & (_count - 1)) == 0;
        _loadCounters = new int[_count];
    }

    /// <summary>
    /// Returns a socket using **round-robin**.
    /// Optimized for single socket and power-of-two pool sizes.
    /// </summary>
    public DealerSocket GetSocket()
    {
        // Hot path: single socket
        if (_count == 1)
            return _sockets[0];

        int index = Interlocked.Increment(ref _currentIndex);

        // Wrap using bitmask if power-of-two
        if (_isPowerOfTwo)
        {
            index &= (_count - 1);
        }
        else
        {
            // wrap-around for non-power-of-two
            if ((uint)index >= _count)
            {
                Interlocked.CompareExchange(ref _currentIndex, 0, index);
                index %= _count; // rare path
            }
        }

        return _sockets[index];
    }

    /// <summary>
    /// Returns a socket using **consistent hash** (e.g., by command key or topic).
    /// </summary>
    public DealerSocket GetSocketByHash(int hash)
    {
        if (_count == 1)
            return _sockets[0];

        int index = hash;

        if (_isPowerOfTwo)
            index &= (_count - 1);
        else
            index = Math.Abs(index % _count);

        return _sockets[index];
    }

    /// <summary>
    /// Returns the socket with the **least load**, optionally incrementing its counter.
    /// </summary>
    public DealerSocket GetLeastLoadedSocket()
    {
        if (_count == 1)
            return _sockets[0];

        int minIndex = 0;
        int minValue = int.MaxValue;

        for (int i = 0; i < _count; i++)
        {
            int load = Volatile.Read(ref _loadCounters[i]);
            if (load < minValue)
            {
                minValue = load;
                minIndex = i;
            }
        }

        // Increment the counter for this socket (lightweight)
        Interlocked.Increment(ref _loadCounters[minIndex]);
        return _sockets[minIndex];
    }

    /// <summary>
    /// Marks a message as completed on a socket (for least-loaded strategy).
    /// </summary>
    public void ReleaseSocket(DealerSocket socket)
    {
        if (_count == 1) return;

        for (int i = 0; i < _count; i++)
        {
            if (_sockets[i] == socket)
            {
                Interlocked.Decrement(ref _loadCounters[i]);
                break;
            }
        }
    }

    /// <summary>
    /// Returns all sockets in the pool.
    /// </summary>
    public IReadOnlyList<DealerSocket> Sockets => _sockets;

}