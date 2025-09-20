using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Microsoft.Extensions.Options;
using NetMQ;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Faster.MessageBus.Shared;

/// <summary>
/// Represents the Local endpoint in the mesh network, including its bound RPC and PUB sockets.
/// </summary>
public class LocalEndpoint
{
    public string MeshId;
    public ushort PubPort { get; internal set; }
    public ushort RpcPort { get; internal set; }
    public string Address { get; internal set; } = GetIPv4();
    public string ApplicationName { get; internal set; }

    /// <summary>
    /// Initializes and binds the Local RPC and PUB sockets to available ports.
    /// </summary>
    public LocalEndpoint(IOptions<MessageBrokerOptions> options)
    {
        // Example values
        string appId = options.Value.ApplicationName;
        string workstation = Environment.MachineName;
        ApplicationName = options.Value.ApplicationName;
        MeshId = $"{workstation}-{appId}";
    }

    /// <summary>
    /// Finds a reliable IPv4 address for use on a local area network (LAN),
    /// specifically designed for offline or "off-the-grid" environments.
    /// It prioritizes active Ethernet and Wi-Fi adapters.
    /// </summary>
    /// <returns>A string containing the LAN IP address, or a fallback to 127.0.0.1.</returns>
    public static string GetIPv4()
    {
        try
        {
            // Get all network interfaces on the machine
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                // We're interested in interfaces that are currently operational
                // and are of a type that is likely to be the main LAN connection.
                if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                    (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                {
                    // Get the IP properties for this interface
                    var ipProperties = networkInterface.GetIPProperties();

                    // Find the first valid IPv4 address on this interface
                    foreach (var ipAddressInfo in ipProperties.UnicastAddresses)
                    {
                        if (ipAddressInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            // We found a suitable address, return it.
                            return ipAddressInfo.Address.ToString();
                        }
                    }
                }
            }
        }
        catch
        {
            // Handle potential exceptions if network info is unavailable
        }

        // If we couldn't find a suitable LAN IP, fallback to loopback.
        return "127.0.0.1";
    }

    internal MeshInfo GetMesh()
    {
        return new MeshInfo
        {
            Address = Address,
            PubPort = PubPort,
            RpcPort = RpcPort,
            ApplicationName = ApplicationName,
            WorkstationName = Environment.MachineName
        };
    }
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
    public static ushort FindAvailablePort(ushort startPort, Action<int> bindAction)
    {
        lock (_lock)
        {
            if (startPort == 0)
            {
                startPort = 10000;
            }

            var endPort = startPort + 2000;
            for (var port = startPort; port <= endPort; port++)
            {
                if (_usedPorts.Contains(port))
                {
                    continue;
                }

                if (IsPortAvailable(bindAction, port))
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
    private static bool IsPortAvailable(Action<int> bindAction, int port)
    {
        try
        {
            // Try to open a listener, then close immediately
            bindAction(port);
            return true;
        }
        catch (SocketException)
        {           
            return false;
        }
        catch (NetMQException)
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
