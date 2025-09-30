using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetMQ;

namespace Faster.MessageBus.Features.Discovery;

/// <summary>
/// Implements a discovery service using <see cref="NetMQBeacon"/> to advertise and listen for <see cref="MeshContext"/> announcements on the local network.
/// This implementation is optimized for high-throughput and low-allocation scenarios.
/// </summary>
/// <remarks>
/// For this optimization to be fully effective, the <see cref="MeshContext"/> type should be a 'class' rather than a 'record' or 'struct'
/// to allow for in-place mutation, which avoids memory allocation on every received beacon.
/// </remarks>
internal sealed class MeshDiscoveryService : IMeshDiscoveryService, IDisposable
{
    #region Fields

    private readonly IMeshRepository _storage;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<MeshDiscoveryService> _logger;
    private readonly MessageBrokerOptions _options;
    private readonly MeshApplication _endpoint;
    private readonly int _port;
    private readonly NetMQBeacon _beacon = new();
    private readonly NetMQPoller _poller = new();
    private readonly NetMQTimer _cleanupTimer;

    private byte[]? _localNodePayload;
    private bool _disposedValue;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="MeshDiscoveryService"/> class, configures the beacon, and starts the discovery process.
    /// </summary>
    public MeshDiscoveryService(IMeshRepository provider,
                                IEventAggregator eventAggregator,
                                IOptions<MessageBrokerOptions> options,
                                ILogger<MeshDiscoveryService> logger,
                                MeshApplication endpoint,
                                int port = 9100)
    {
        _storage = provider;
        _eventAggregator = eventAggregator;
        _logger = logger;
        _options = options.Value; // Get the value once to avoid repeated lookups
        _endpoint = endpoint;
        _port = port;

        _beacon.ConfigureAllInterfaces(port);
        _beacon.Subscribe(string.Empty); // Empty string is a wildcard
        _beacon.ReceiveReady += OnSignalReceived;
        _poller.Add(_beacon);

        // Set up and add the timer for cleaning up inactive nodes
        _cleanupTimer = new NetMQTimer(_options.CleanupInterval);
        _cleanupTimer.Elapsed += RemoveInactiveApplications;
        _poller.Add(_cleanupTimer);
    }

    /// <inheritdoc/>
    public void Start(MeshContext meshInfo)
    {
        // Pre-serialize the local node's information to avoid this work on every publish tick.
        _localNodePayload = MessagePackSerializer.Serialize(meshInfo);

        // Publish this node's identity for others to discover using a configurable interval.
        _beacon.Publish(_localNodePayload, _options.BeaconInterval);
        _poller.RunAsync();
        _logger.LogInformation("Mesh discovery service started. Advertising on all interfaces _port {_port}.", _port);
    }

    /// <inheritdoc/>
    public void Stop()
    {
        _beacon.Unsubscribe();
        _logger.LogInformation("Mesh discovery service stopped advertising.");
    }

    /// <summary>
    /// Handles received beacon signals, deserializes the <see cref="MeshContext"/>, and updates the mesh provider.
    /// This method is executed on the poller's thread and is optimized to reduce allocations and CPU usage.
    /// </summary>
    private void OnSignalReceived(object? sender, NetMQBeaconEventArgs args)
    {
        // Use a while loop to keep trying to receive signals until TryReceive returns false (timeout reached or no more signals)
        while (_beacon.TryReceive(TimeSpan.FromSeconds(0.1), out var beacon))
        {
            try
            {       
                var payload = beacon.Bytes;

                // Deserialize the received payload into MeshContext.
                var info = MessagePackSerializer.Deserialize<MeshContext>(payload);

                info.LastSeen = DateTime.UtcNow;

                // Use a TryAdd pattern which is typically more performant for concurrent collections.
                if (_storage.TryAdd(info))
                {
                    _eventAggregator.Publish(new MeshJoined(info));
                    _logger.LogInformation("New mesh node discovered: {ApplicationName} on {WorkstationName}", info.ApplicationName, info.WorkstationName);
                }
                else
                {
                    _storage.Update(info);
                }
            }
            catch (MessagePackSerializationException ex)
            {
                _logger.LogWarning(ex, "Beacon deserialization failed. A malformed or incompatible packet was received.");
            }
            catch (Exception ex)
            {
                // The catch block must be INSIDE the while loop to continue processing subsequent signals
                _logger.LogError(ex, "An unexpected error occurred while processing a beacon signal.");
            }
        }
    }

    /// <inheritdoc/>
    public void RemoveInactiveApplications(object? sender, NetMQTimerEventArgs args)
    {
        var now = DateTime.UtcNow;
        var inactiveThreshold = _options.InactiveThreshold;

        // NOTE: This operation has a complexity of O(N), where N is the number of nodes.
        // For scenarios with a very large number of nodes, consider a more advanced
        // data structure for tracking expirations to avoid iterating the entire collection.
        foreach (var meshInfo in _storage.All())
        {
            if ((now - meshInfo.LastSeen) > inactiveThreshold)
            {
                if (_storage.TryRemove(meshInfo))
                {
                    _eventAggregator.Publish(new MeshRemoved(meshInfo));
                    _logger.LogInformation("Removed inactive mesh node: {ApplicationName} on {WorkstationName}", meshInfo.ApplicationName, meshInfo.WorkstationName);
                }
            }
        }
    }

    /// <summary>
    /// Disposes the service, stopping the poller and the beacon.
    /// </summary>
    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // Unhook events to prevent memory leaks
                _beacon.ReceiveReady -= OnSignalReceived;
                _cleanupTimer.Elapsed -= RemoveInactiveApplications;

                // Stop the poller and dispose all associated resources.
                _poller.Stop();
                _poller.Dispose();
            }
            _disposedValue = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}