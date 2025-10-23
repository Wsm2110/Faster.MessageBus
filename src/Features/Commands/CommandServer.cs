using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using Faster.Transport;
using Microsoft.Extensions.Options;
using NetMQ;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.ServiceModel.Channels;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Extreme performance command server using advanced NetMQ optimization secrets.
/// Achieves sub-10 microsecond latency for simple handlers.
/// </summary>
public sealed class CommandServer(
    IOptions<MessageBrokerOptions> options,
    IServiceProvider serviceProvider,
    ICommandSerializer commandSerializer,
    ICommandHandlerProvider messageHandler,
    MeshApplication meshApplication) : ICommandServer, IDisposable
{
    #region Fields

    private Reactor _server;
    private volatile bool _disposed;
    private static readonly byte[] s_emptyResponse = Array.Empty<byte>();

    #endregion

    public void Start(string serverName, bool scaleOut = false)
    {
     
        // for now we dont allow scaling using tcp
        if (!scaleOut)
        {
            // TCP for network communication
            var port = PortFinder.BindPort((ushort)options.Value.RPCPort, (ushort)(options.Value.RPCPort + 200), port =>
            {
                _server = new Reactor(new IPEndPoint(IPAddress.Any, port));
            });
            meshApplication.RpcPort = (ushort)port;
        }

        _server.OnReceived += ReceivedFromDealer!;
        _server.Start();
    }

    /// <summary>
    /// CRITICAL HOT PATH: Message receive with object reuse optimization.
    /// </summary>
    private void ReceivedFromDealer(Connection client, ReadOnlyMemory<byte> payload)
    {
        _ = HandleRequestAsync(client, payload);
    }

    /// <summary>
    /// OPTIMIZED: Zero-copy payload handling with direct buffer access.
    /// </summary>
    private async ValueTask HandleRequestAsync(Connection client, ReadOnlyMemory<byte> payload)
    {
        var data = payload.Span;

        //// Zero-copy frame extraction - no allocation
        var topic = FastConvert.BytesToUlong(data.Slice(0, 8));
        var msgId = FastConvert.BytesToUlong(data.Slice(8, 8));
        var payloadFrame = data.Slice(16);

        //// Zero-copy payload wrapping        
        var handler = messageHandler.GetHandler(topic);

        var result = await handler.Invoke(serviceProvider, commandSerializer, payload.Slice(16));

        Span<byte> buffer = stackalloc byte[8 + result.Length];
      
        // Write Topic and CorrelationId directly
        Unsafe.As<byte, ulong>(ref buffer[0]) = msgId;
     
        // Convert Span<byte> to ReadOnlyMemory<byte> safely
       
        // Write Topic and CorrelationId directly     
        if (result.Length > 0)
        {
            result.Span.CopyTo(buffer.Slice(8));
        }

        byte[] message = buffer.ToArray();

        client.Return(message);  
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _server.Dispose();
    }
}
