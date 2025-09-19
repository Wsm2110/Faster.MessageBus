using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Features.Heartbeat.Contracts;
using NetMQ;

namespace Faster.MessageBus.Features.Heartbeat
{
    /// <summary>
    /// Periodically monitors the health of mesh nodes by checking for expired entries
    /// and removing inactive nodes from the discovery service.
    /// </summary>
    internal class HeartBeatMonitor : IHeartBeatMonitor, IDisposable
    {
        /// <summary>
        /// The service responsible for storing and managing information about nodes in the mesh.
        /// </summary>
        private readonly IMeshDiscoveryService _discoveryService;

        /// <summary>
        /// The NetMQ event loop that drives the timer.
        /// </summary>
        private readonly INetMQPoller _poller;

        /// <summary>
        /// The timer that periodically triggers the check for inactive nodes.
        /// </summary>
        private NetMQTimer _timer;

        private bool disposedValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="HeartBeatMonitor"/> class.
        /// </summary>
        /// <param name="discoveryService">The discovery service used to track and manage mesh nodes.</param>
        public HeartBeatMonitor(IMeshDiscoveryService discoveryService)
        {
            _discoveryService = discoveryService;

            // Create a timer that will trigger the cleanup method at regular intervals.
            _timer = new NetMQTimer(TimeSpan.FromMilliseconds(100));
            _timer.Elapsed += _discoveryService.RemoveInactiveApplications;

            // The poller is a dedicated background thread that manages timers and sockets.
            _poller = new NetMQPoller { _timer };
            Start();
        }

        /// <summary>
        /// Starts the NetMQ poller in the background, which begins the periodic heartbeat checks.
        /// </summary>
        public void Start()
        {
            _poller.RunAsync();
        }

        /// <summary>
        /// Asynchronously stops the NetMQ poller, halting all heartbeat checks.
        /// </summary>
        public void Stop()
        {
            _poller.StopAsync();
        }

        /// <summary>
        /// Cleans up the resources used by the monitor.
        /// </summary>
        /// <param name="disposing">True if called from <see cref="Dispose()"/>; false if called from a finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // Unsubscribe the event handler to prevent memory leaks.
                    _timer.Elapsed -= _discoveryService.RemoveInactiveApplications;

                    // Stop and dispose the poller thread and its associated resources.
                    _poller.StopAsync();
                    _poller.Dispose();
                }

                disposedValue = true;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method.
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}