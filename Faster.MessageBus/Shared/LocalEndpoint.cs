using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;

namespace Faster.MessageBus.Shared;

/// <summary>
/// Represents the Local endpoint in the mesh network, including its bound RPC and PUB sockets.
/// </summary>
public class LocalEndpoint
{
    /// <summary>
    /// Gets the port number bound to the RPC Socket.
    /// </summary>
    public ushort RpcPort { get; private set; }

    /// <summary>
    /// Gets the port number bound to the PUB Socket.
    /// </summary>
    public ushort PubPort { get; private set; }

    /// <summary>
    /// Initializes and binds the Local RPC and PUB sockets to available ports.
    /// </summary>
    public LocalEndpoint(IOptions<MessageBusOptions> options)
    {
        RpcPort = PortFinder.FindAvailablePort();
        PubPort = PortFinder.FindAvailablePort();

        // Example values
        string appId = options.Value.ApplicationName;  // Could be configurable
        string workstation = Environment.MachineName;

        var id = $"{workstation}-{appId}";
        Meshinfo = CreateMeshInfo(id, workstation, appId);
    }

    public MeshInfo Meshinfo { get; set; }

    public void RegisterSelf()
    {
        EventAggregator.Publish(new MeshJoined(Meshinfo));
    }

    public static string GetLocalIPv4()
    {
        foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            {
                return ip.ToString(); // Return the first valid IPv4 address
            }
        }

        return "127.0.0.1"; // Fallback
    }

    /// <summary>
    /// Creates a <see cref="MeshInfo"/> object representing this endpoint.
    /// </summary>
    /// <param name="meshId">Unique identifier for the mesh node.</param>
    /// <returns>A new <see cref="MeshInfo"/> instance.</returns>
    private MeshInfo CreateMeshInfo(string meshId, string workstationName, string appID) => new(meshId, workstationName, GetLocalIPv4(), appID, RpcPort, PubPort);
}

/// <summary>
/// Provides functionality to locate an available TCP port,
/// and tracks which ports have already been handed out.
/// </summary>
public static class PortFinder
{
    // Tracks ports that have already been returned
    private static readonly HashSet<int> _usedPorts = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Finds an available TCP port in the specified range,
    /// skipping any ports already returned by this class.
    /// </summary>
    /// <param name="startPort">Initialize of the port range (inclusive).</param>
    /// <param name="endPort">End of the port range (inclusive).</param>
    /// <returns>An available port number.</returns>
    public static ushort FindAvailablePort(ushort startPort = 5000, ushort endPort = 5200)
    {
        lock (_lock)
        {
            for (ushort port = startPort; port <= endPort; port++)
            {
                if (_usedPorts.Contains(port))
                    continue;

                if (IsPortAvailable(port))
                {
                    _usedPorts.Add(port);
                    return port;
                }
            }
        }

        throw new InvalidOperationException("No available ports found in range.");
    }

    /// <summary>
    /// Checks if a specific TCP port is available on the Local machine.
    /// </summary>
    /// <param name="port">Port number to check.</param>
    /// <returns><c>true</c> if the port is not in use by any other process; otherwise, <c>false</c>.</returns>
    private static bool IsPortAvailable(int port)
    {
        try
        {
            // Try to open a listener, then close immediately
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch
        {
            // In case of any other error, assume not available
            return false;
        }
    }

    /// <summary>
    /// Clears the internal record of used ports, so they can be returned again.
    /// </summary>
    public static void ResetUsedPorts()
    {
        lock (_lock)
        {
            _usedPorts.Clear();
        }
    }
}
