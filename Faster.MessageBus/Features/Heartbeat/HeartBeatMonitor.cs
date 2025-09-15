using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Features.Heartbeat.Contracts;
using NetMQ;

namespace Faster.MessageBus.Features.Heartbeat
{ 
    /// <summary>
    /// Periodically checks for expired MeshInfo entries and removes them from the mesh storage.
    /// </summary>
    internal class HeartBeatMonitor : IHeartBeatMonitor, IDisposable
    {
        private readonly IMeshDiscoveryService _discoveryService;     // Storage of all known mesh nodes
        private readonly INetMQPoller _poller;      // NetMQ event loop
        private bool disposedValue;
        private NetMQTimer _timer;

        /// <summary>
        /// Initializes a new instance of the <see cref="HeartBeatMonitor"/> class.
        /// </summary>
        /// <param name="storage">Mesh storage to track mesh nodes.</param>
        public HeartBeatMonitor(IMeshDiscoveryService discoveryService)
        {
            _discoveryService = discoveryService;

            // Timer triggers every 60 seconds to check for expired nodes
            _timer = new NetMQTimer(TimeSpan.FromSeconds(5));
            _timer.Elapsed += _discoveryService.RemoveInactiveApplications;

            // Attach the timer to the poller
            _poller = new NetMQPoller { _timer };
        }

        /// <summary>
        /// Starts the NetMQ poller and begins periodic heartbeat checks.
        /// </summary>
        public void Start()
        {
            _poller.RunAsync();
        }

        /// <summary>
        /// Stops the NetMQ poller and halts heartbeat checks.
        /// </summary>
        public void Stop()
        {
            _poller.StopAsync();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _timer.Elapsed -= _discoveryService.RemoveInactiveApplications;
                    _poller.StopAsync();
                    _poller.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
