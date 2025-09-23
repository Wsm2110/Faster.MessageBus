using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Heartbeat.Contracts;

/// <summary>
/// Monitors the heartbeat of known mesh nodes and removes inactive ones.
/// </summary>
internal interface IHeartBeatMonitor : IDisposable
{
    /// <summary>
    /// Starts the heartbeat monitor.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the heartbeat monitor.
    /// </summary>
    void Stop();
}

