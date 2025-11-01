using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using Faster.Transport;
using Faster.Transport.Contracts;
using Faster.Transport.Features.Tcp;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Extreme-performance CommandServer with three reactors (TCP, IPC, Inproc).
/// Each server runs on its own dedicated thread to minimize latency and contention.
/// </summary>
public sealed class CommandServer(
    IOptions<MessageBrokerOptions> options,
    IServiceProvider serviceProvider,
    ICommandSerializer commandSerializer,
    ICommandHandlerProvider messageHandler,
    MeshApplication meshApplication) : ICommandServer, IDisposable
{
    private Reactor _tcpServer;
    private IParticle _ipcServer;
    private IParticle _inprocServer;
    private Thread _tcpThread;
    private Thread _ipcThread;
    private Thread _inprocThread;
    private volatile bool _disposed;

    private static readonly byte[] s_emptyResponse = Array.Empty<byte>();

    // ===========================
    // Startup
    // ===========================
    public void Start(string serverName, bool scaleOut = false)
    {
        var opts = options.Value;
        var appName = opts.ApplicationName;

        // Create shared handler delegate to avoid extra delegate allocations
        Action<IParticle, ReadOnlyMemory<byte>> recv = ReceivedFromDealer;

        // ---------------- TCP ----------------
        var port = PortFinder.BindPort(
            (ushort)opts.RPCPort,
            (ushort)(opts.RPCPort + 200),
            port => _tcpServer = new Reactor(new IPEndPoint(IPAddress.Any, port))
        );

        _tcpServer.OnReceived = recv;
        _tcpThread = new Thread(_tcpServer.Start)
        {
            IsBackground = true,
            Name = "Reactor-TCP"
        };
        _tcpThread.Start();
        meshApplication.RpcPort = (ushort)port;
                
        _ipcThread = new Thread(() =>
        {
          _ipcServer = new ParticleBuilder()
          .UseMode(TransportMode.Ipc)
          .WithChannel(appName, isServer: true)
          .OnReceived(recv)
          .Build();
        })
        {
            IsBackground = true,
            Name = "Reactor-IPC"
        };
        _ipcThread.Start();

        // ---------------- INPROC ----------------


        _inprocThread = new Thread(() =>
        {
            _inprocServer = new ParticleBuilder()
                .UseMode(TransportMode.Inproc)
                .WithChannel(appName, isServer: true)
                .OnReceived(recv)
                .Build();
        })
        {
            IsBackground = true,
            Name = "Reactor-Inproc"
        };
        _inprocThread.Start();
    }

    // ===========================
    // Hot Path Message Handling
    // ===========================
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReceivedFromDealer(IParticle client, ReadOnlyMemory<byte> payload)
    {
        // Fire-and-forget to avoid blocking I/O thread
        _ = HandleRequestAsync(client, payload);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask HandleRequestAsync(IParticle client, ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;

        // Fast zero-copy header parse
        ulong topic = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AsRef(span[0]));
        ulong msgId = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AsRef(span[8]));
        var body = payload.Slice(16);

        // Lookup command handler
        var handler = messageHandler.GetHandler(topic);
        if (handler is null)
        {
            client.Send(s_emptyResponse);
            return;
        }

        // Execute handler (async/sync supported)
        var result = await handler.Invoke(serviceProvider, commandSerializer, body).ConfigureAwait(false);

        // Allocate response from pool (8 bytes header + payload)
        int totalLen = 8 + result.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(totalLen);

        try
        {
            var buf = rented.AsSpan(0, totalLen);
            Unsafe.WriteUnaligned(ref buf[0], msgId);

            if (result.Length > 0)
                result.Span.CopyTo(buf.Slice(8));

            client.Send(buf.Slice(0, totalLen));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // ===========================
    // Cleanup
    // ===========================
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tcpServer?.Dispose();
        _ipcServer?.Dispose();
        _inprocServer?.Dispose();

        _tcpThread?.Join(100);
        _ipcThread?.Join(100);
        _inprocThread?.Join(100);
    }
}
