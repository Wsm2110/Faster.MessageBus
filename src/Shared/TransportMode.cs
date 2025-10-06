using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Shared;

/// <summary>
/// Transport modes for NetMQ communication.
/// </summary>
public enum TransportMode
{
    /// <summary>
    /// TCP transport - works across network, slowest but most flexible.
    /// Latency: 10-50 μs (localhost), 100-500 μs (network)
    /// </summary>
    Tcp = 0,

    /// <summary>
    /// IPC transport - Unix domain sockets (Linux) or named pipes (Windows).
    /// Only works on same machine, 10x faster than TCP loopback.
    /// Latency: 1-2 μs
    /// </summary>
    Ipc = 1,

    /// <summary>
    /// Inproc transport - shared memory within same process.
    /// Fastest possible, but only works between threads in same process.
    /// Latency: 0.1-0.2 μs (100-200 ns)
    /// </summary>
    Inproc = 2
}

