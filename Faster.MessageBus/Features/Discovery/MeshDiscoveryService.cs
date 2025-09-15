using Faster.MessageBus.Features.Discovery.Contracts;
using Faster.MessageBus.Shared;
using MessagePack;
using Microsoft.Extensions.Options;
using NetMQ;

namespace Faster.MessageBus.Features.Discovery;

/// <summary>
/// Implements a discovery service using <see cref="NetMQBeacon"/> to advertise and listen for <see cref="MeshInfo"/> announcements on the local network.
/// </summary>
internal class MeshDiscoveryService : IMeshDiscoveryService, IDisposable
{
    #region Fields

    /// <summary>
    /// The storage provider used to track the state of discovered nodes in the mesh.
    /// </summary>
    private readonly IMeshRepository _storage;

    /// <summary>
    /// Configuration options for the message bus.
    /// </summary>
    private readonly IOptions<MessageBusOptions> _options;

    /// <summary>
    /// Provides the details of the local node's endpoint to be advertised.
    /// </summary>
    private readonly LocalEndpoint _endpoint;

    /// <summary>
    /// The underlying NetMQ beacon used for UDP broadcast and discovery.
    /// </summary>
    private readonly NetMQBeacon _beacon = new();

    /// <summary>
    /// A flag to track the disposal state of the object to prevent redundant calls.
    /// </summary>
    private bool _disposedValue;

    /// <summary>
    /// The poller that runs the beacon's event loop in a background thread.
    /// </summary>
    private readonly NetMQPoller _poller = new();

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="MeshDiscoveryService"/> class, configures the beacon, and starts the discovery process.
    /// </summary>
    /// <param name="provider">The mesh provider used to track discovered nodes.</param>
    /// <param name="options">The configuration options for the message bus.</param>
    /// <param name="endpoint">The local endpoint information to advertise over the network.</param>
    /// <param name="port">The UDP port to use for beacon broadcasting and listening.</param>
    public MeshDiscoveryService(IMeshRepository provider, IOptions<MessageBusOptions> options, LocalEndpoint endpoint, int port = 9100)
    {
        _storage = provider;
        _options = options;
        _endpoint = endpoint;

        // Configure the beacon to use all available network interfaces on the specified port.
        _beacon.ConfigureAllInterfaces(port);
        // Subscribe to all incoming beacons (the empty string is a wildcard).
        _beacon.Subscribe("");
        _beacon.ReceiveReady += OnSignalReceived!; // Hook the receive event.
        _poller.Add(_beacon);

        // Start the service upon initialization.
        Start();
    }

    /// <summary>
    /// Starts the discovery service by publishing the local node's information and running the background poller to listen for other nodes.
    /// </summary>
    public void Start()
    {
        _endpoint.RegisterSelf();
        // Publish this node's identity for others to discover.
        _beacon.Publish(MessagePackSerializer.Serialize(_endpoint.Meshinfo));
        _poller.RunAsync();
    }

    /// <summary>
    /// Stops the discovery service by unsubscribing from beacons and disposing the beacon instance.
    /// </summary>
    /// <remarks>
    /// For a complete shutdown and resource cleanup, it is recommended to call <see cref="Dispose()"/>.
    /// </remarks>
    public void Stop()
    {
        _beacon.Unsubscribe();
        _beacon.Dispose();
        // Note: The poller is intentionally not stopped here; Dispose() handles the full shutdown.
    }

    /// <summary>
    /// Handles received beacon signals, deserializes the <see cref="MeshInfo"/>, and updates the mesh provider.
    /// This method is executed on the poller's thread.
    /// </summary>
    /// <param name="sender">The sender of the event (the <see cref="NetMQBeacon"/>).</param>
    /// <param name="args">The event arguments containing the received signal.</param>
    private void OnSignalReceived(object sender, NetMQBeaconEventArgs args)
    {
        try
        {
            if (!_beacon.TryReceive(TimeSpan.FromSeconds(1), out var payload))
            {
                return;
            }

            // Deserialize the received payload into MeshInfo and update its LastSeen timestamp.
            var info = MessagePackSerializer.Deserialize<MeshInfo>(payload.Bytes);
            var updated = info with { LastSeen = DateTime.UtcNow };

            // If the node is new, add it and publish a MeshJoined event. Otherwise, update its state.
            if (_storage.Add(updated))
            {
                EventAggregator.Publish(new MeshJoined(updated));
            }
            else
            {
                _storage.Update(updated);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Beacon deserialization failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void RemoveInactiveApplications(object? sender, NetMQTimerEventArgs args)
    {
        var now = DateTime.UtcNow;

        foreach (var meshInfo in _storage.All())
        {
            // If the node hasn't been seen in the last 10 seconds, consider it inactive.
            if ((now - meshInfo.LastSeen).TotalSeconds > 10)
            {
                _storage.Remove(meshInfo);
                EventAggregator.Publish(new MeshRemoved(meshInfo));
            }
        }
    }

    /// <summary>
    /// Disposes the service, stopping the poller and the beacon.
    /// </summary>
    /// <param name="disposing"><c>true</c> if called from <see cref="Dispose()"/>; <c>false</c> if called from the finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // Stop the poller and beacon to clean up resources.
                _poller.Stop();
                _poller.Dispose();
                Stop();
            }
            _disposedValue = true;
        }
    }

    /// <summary>
    /// Disposes the discovery service and its underlying resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}