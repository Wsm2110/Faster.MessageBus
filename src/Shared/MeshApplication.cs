using Faster.MessageBus.Contracts;
using Microsoft.Extensions.Options;
using NetMQ;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Faster.MessageBus.Shared
{
    /// <summary>
    /// Represents the local endpoint in the mesh network.
    /// Holds configuration and state for RPC and PUB sockets, the node's mesh ID,
    /// routing table, and network information.
    /// </summary>
    public class MeshApplication : IMeshApplication
    {
        /// <summary>
        /// Unique mesh identifier for this node, generated randomly on startup.
        /// </summary>
        public ulong MeshId = new WyRandom((ulong)Environment.TickCount).NextInt64();

        /// <summary>
        /// The TCP _port used for PUB socket binding.
        /// </summary>
        public ushort PubPort { get; internal set; }

        /// <summary>
        /// The TCP _port used for RPC socket binding.
        /// </summary>
        public ushort RpcPort { get; internal set; }

        /// <summary>
        /// The LAN IPv4 address of this node.
        /// </summary>
        public string Address { get; internal set; } = GetIPv4();

        /// <summary>
        /// The application name for identification in the mesh network.
        /// </summary>
        public string ApplicationName { get; internal set; } = "Unknown";

        /// <summary>
        /// The logical cluster name for grouping nodes.
        /// </summary>
        public string ClusterName { get; set; }

        /// <summary>
        /// The command routing table containing topic hashes this node handles.
        /// </summary>
        public ulong[] CommandRoutingTable { get; set; }

        /// <summary>
        /// Initializes a new MeshApplication instance and sets its application and cluster context.
        /// </summary>
        /// <param name="options">Configuration options for the message broker.</param>
        public MeshApplication(IOptions<MessageBrokerOptions> options)
        {
            if (!string.IsNullOrEmpty(options.Value.ApplicationName))
            {
                ApplicationName = options.Value.ApplicationName;
            }

            ClusterName = options.Value.Cluster.ClusterName;
        }

        /// <summary>
        /// Finds a reliable IPv4 address for the node, prioritizing active LAN adapters.
        /// Falls back to 127.0.0.1 if no suitable address is found.
        /// </summary>
        /// <returns>The IPv4 address as a string.</returns>
        public static string GetIPv4()
        {
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    {
                        var ipProperties = networkInterface.GetIPProperties();
                        foreach (var ipAddressInfo in ipProperties.UnicastAddresses)
                        {
                            if (ipAddressInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                return ipAddressInfo.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Swallow exceptions, fallback to loopback.
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// Returns a <see cref="MeshContext"/> instance representing this node's state,
        /// including routing information and identification.
        /// </summary>
        /// <param name="routingTable">Command routing table as a byte array.</param>
        /// <param name="self">Indicates whether this context represents the local node.</param>
        /// <returns>A <see cref="MeshContext"/> populated with this node's information.</returns>
        internal MeshContext GetMeshContext(byte[]? routingTable = null, bool self = false)
        {
            return new MeshContext
            {
                MeshId = MeshId,
                Address = Address,
                PubPort = PubPort,
                RpcPort = RpcPort,
                ApplicationName = ApplicationName,
                WorkstationName = Environment.MachineName,
                ClusterName = ClusterName,
                Self = self,
                CommandRoutingTable = routingTable
            };
        }
    }

    /// <summary>
    /// Utility class for locating and binding available TCP ports in a thread- and process-safe manner.
    /// Uses a global mutex to prevent _port collisions across multiple processes.
    /// </summary>
    public static class PortFinder
    {
        private const string PortFinderMutexName = "Global\\Faster.Messagebus";

        /// <summary>
        /// Attempts to find a free TCP _port within the specified range and binds it using the provided action.
        /// </summary>
        /// <param name="startPort">Starting _port in the range.</param>
        /// <param name="endPort">Ending _port in the range.</param>
        /// <param name="bindAction">Action that binds or tests a _port.</param>
        /// <returns>The first available _port successfully bound.</returns>
        /// <exception cref="TimeoutException">Thrown if the global mutex cannot be acquired in 10 seconds.</exception>
        /// <exception cref="InvalidOperationException">Thrown if no ports are available in the specified range.</exception>
        public static int BindPort(ushort startPort, ushort endPort, Action<int> bindAction)
        {
            using (var mutex = new Mutex(false, PortFinderMutexName))
            {
                try
                {
                    if (!mutex.WaitOne(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Could not acquire mutex for _port finding.");
                    }

                    for (var port = startPort; port <= endPort; port++)
                    {
                        try
                        {
                            bindAction(port);
                            return port; // success
                        }
                        catch (Exception)
                        {
                            continue; // _port taken, try next
                        }
                    }
                }
                finally
                {
                    mutex.ReleaseMutex(); // always release
                }
            }

            throw new InvalidOperationException("No available ports found in the specified range.");
        }
    }
}
