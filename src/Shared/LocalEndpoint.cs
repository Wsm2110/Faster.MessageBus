using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Scope.Cluster;
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
    public ulong MeshId = new WyRandom((ulong)Environment.TickCount).NextInt64();
    public ushort PubPort { get; internal set; }
    public ushort RpcPort { get; internal set; }
    public string Address { get; internal set; } = GetIPv4();
    public string ApplicationName { get; internal set; } = "Unknown";
    public string ClusterName { get; set; }

    /// <summary>
    /// Initializes and binds the Local RPC and PUB sockets to available ports.
    /// </summary>
    public LocalEndpoint(IOptions<MessageBrokerOptions> options)
    {
        // Example values
        string appId = options.Value.ApplicationName;
        string workstation = Environment.MachineName;

        if (!string.IsNullOrEmpty(options.Value.ApplicationName))
        {
            ApplicationName = options.Value.ApplicationName;
        }

        ClusterName = options.Value.Cluster.ClusterName;
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

    internal MeshInfo GetMesh(bool self = false)
    {
        return new MeshInfo
        {
            MeshId = MeshId,
            Address = Address,
            PubPort = PubPort,
            RpcPort = RpcPort,
            ApplicationName = ApplicationName,
            WorkstationName = Environment.MachineName,
            ClusterName = ClusterName, 
            Self = self
        };
    }
}

/// <summary>
/// Provides functionality to locate an available TCP port,
/// and tracks which ports have already been handed out.
/// </summary>
public static class PortFinder
{
    // A name for the system-wide mutex. The "Global\" prefix is important.
    private const string PortFinderMutexName = "Global\\Faster.Messagebus";

    public static int FindAndBindPortWithMutex(ushort startPort, ushort endPort, Action<int> bindAction)
    {
        using (var mutex = new Mutex(false, PortFinderMutexName))
        {
            try
            {
                // 1. Wait until we can acquire the mutex lock.
                if (!mutex.WaitOne(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Could not acquire mutex for port finding.");
                }

                // 2. We now have exclusive access. No other instance can be in here.
                for (var port = startPort; port <= endPort; port++)
                {
                    try
                    {
                        bindAction(port);
                        return port; // Success!
                    }
                    catch (NetMQException)
                    {
                        continue; // Port is taken, try the next one.
                    }
                }
            }
            finally
            {
                // 4. CRITICAL: Always release the mutex.
                mutex.ReleaseMutex();
            }
        }

        throw new InvalidOperationException("No available ports found in the specified range.");
    }
}
