using System;
using System.Runtime.Intrinsics.Arm;

namespace Faster.Transport.Transport
{
    /// <summary>
    /// Factory class for creating transport-specific listener and client instances.
    /// </summary>
    /// <remarks>
    /// This provides a unified entry point for building <see cref="IListener"/> and <see cref="IConnection"/> 
    /// implementations across multiple transport schemes (Inproc, IPC, TCP, etc.).
    /// </remarks>
    public static class TransportFactory
    {
        /// <summary>
        /// Creates a transport listener based on the specified <see cref="TransportEndpoint"/>.
        /// </summary>
        /// <param name="ep">The parsed transport endpoint describing the scheme, host, and port.</param>
        /// <returns>
        /// An instance of <see cref="IListener"/> appropriate for the specified transport scheme.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// Thrown if the transport scheme is not supported or implemented.
        /// </exception>
        public static IListener CreateListener(TransportEndpoint ep)
            => ep.Scheme switch
            {
                TransportScheme.Inproc => new Inproc.InprocListener(ep.HostOrName),
                TransportScheme.Ipc => new Ipc.IpcListener(ep.HostOrName),
                TransportScheme.Tcp => new Tcp.TcpListenerAdapter(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ep.HostOrName), ep.Port)),
                _ => throw new NotSupportedException($"Unsupported scheme: {ep.Scheme}")
            };

        /// <summary>
        /// Creates a transport client based on the specified <see cref="TransportEndpoint"/>.
        /// </summary>
        /// <param name="ep">The parsed transport endpoint describing the scheme, host, and port.</param>
        /// <returns>
        /// An instance of <see cref="IConnection"/> appropriate for the specified transport scheme.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// Thrown if the transport scheme is not supported or implemented.
        /// </exception>
        public static IConnection CreateClient(TransportEndpoint ep)
            => ep.Scheme switch
            {
                TransportScheme.Inproc => new Inproc.InprocClient(ep.HostOrName),
                TransportScheme.Ipc => new Ipc.IpcClient(ep.HostOrName),
                TransportScheme.Tcp => new Tcp.TcpClientAdapter(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ep.HostOrName), ep.Port)),
                _ => throw new NotSupportedException($"Unsupported scheme: {ep.Scheme}")
            };
    }
}
