using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using Faster.Transport;
using Faster.Transport.Contracts;
using Faster.Transport.Features.Tcp;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// ⚡ **CommandServer**
/// 
/// This class is responsible for receiving and processing commands coming from clients
/// (other processes or nodes). It supports three types of connections:
/// 
/// - **TCP** → For communication across different machines.
/// - **IPC** → For communication across processes on the same machine.
/// - **Inproc** → For communication within the same process.
/// 
/// Each transport runs on its **own background thread** to prevent blocking and to maximize throughput.
/// 
/// <para>
/// The server deserializes incoming messages, invokes the appropriate command handler,
/// and sends the response back to the client.  
/// Designed for **extreme performance**, low GC pressure, and microsecond latency.
/// </para>
/// 
/// <example>
/// Example usage:
/// <code>
/// var server = new CommandServer(options, provider, serializer, handlerProvider, meshApp);
/// server.Start("MyServerApp");
/// Console.WriteLine("Command server is running...");
/// </code>
/// </example>
/// </summary>
public sealed class CommandServer(
    IOptions<MessageBrokerOptions> options,
    IServiceProvider serviceProvider,
    ICommandSerializer commandSerializer,
    ICommandHandlerProvider messageHandler,
    MeshApplication meshApplication) : ICommandServer, IDisposable
{

    private Reactor? _tcpServer;
    private IParticle? _ipcServer;
    private IParticle? _inprocServer;
    private Thread? _tcpThread;
    private Thread? _ipcThread;
    private volatile bool _disposed;

    /// <summary>
    /// A pre-allocated empty byte array for cases where a handler isn’t found.
    /// We reuse this to avoid allocating new empty arrays each time.
    /// </summary>
    private static readonly byte[] s_emptyResponse = Array.Empty<byte>();

    /// <summary>
    /// A thread-local reusable buffer to avoid allocating a new byte array on every message.
    /// Each thread has its own buffer to prevent race conditions.
    /// </summary>
    [ThreadStatic]
    private static byte[]? s_responseBuffer;
 
    /// <summary>
    /// Starts the CommandServer and initializes the TCP, IPC, and Inproc reactors.
    /// Each reactor runs on a separate background thread for maximum parallelism.
    /// </summary>
    /// <param name="serverName">A friendly name for this server instance (used for identification).</param>
    /// <param name="scaleOut">If true, allows scaling out across multiple nodes.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Start(string serverName, bool scaleOut = false)
    {
        var opts = options.Value;
        var appName = opts.ApplicationName;

        // ================= TCP Reactor =================
        // Find an available port and create the TCP server reactor.
        var port = PortFinder.BindPort(
            (ushort)opts.RPCPort,
            (ushort)(opts.RPCPort + 200),
            port => _tcpServer = new Reactor(new IPEndPoint(IPAddress.Any, port))
        );

        // When the TCP server receives a message, it calls ReceivedFromDealer().
        _tcpServer!.OnReceived = ReceivedFromDealer;

        // Start the TCP reactor in its own thread.
        _tcpThread = new Thread(_tcpServer.Start)
        {
            IsBackground = true,
            Name = "Reactor-TCP"
        };
        _tcpThread.Start();

        // Record which port we bound to.
        meshApplication.RpcPort = (ushort)port;
            
        // IPC communication (same machine, different processes)
        _ipcThread = new Thread(() =>
        {
            _ipcServer = new ParticleBuilder()
                .UseMode(TransportMode.Ipc)
                .WithChannel(appName, isServer: true)
                .OnReceived(ReceivedFromDealer)
                .Build();
        })
        {
            IsBackground = true,
            Name = "Reactor-IPC"
        };

        _ipcThread.Start();
          
        // In-process communication (same app)
        _inprocServer = new ParticleBuilder()
            .UseMode(TransportMode.Inproc)
            .WithChannel(appName, isServer: true)
            .OnReceived(ReceivedFromDealer)
            .Build();
    }

    /// <summary>
    /// Called whenever a message is received from a client (via TCP, IPC, or Inproc).
    /// This method *immediately* offloads work to a background thread to avoid blocking
    /// the network I/O thread (which must stay responsive).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReceivedFromDealer(IParticle client, ReadOnlyMemory<byte> payload)
    {
        // We don't await here — we just schedule the work asynchronously.
        _ = HandleRequestAsync(client, payload);
    }

    /// <summary>
    /// Handles the actual command logic:
    /// 1. Reads the message header (topic + message ID)
    /// 2. Finds the command handler
    /// 3. Invokes it
    /// 4. Sends the response back
    /// </summary>
    /// <param name="client">The client that sent the message.</param>
    /// <param name="payload">The raw message data (header + body).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask HandleRequestAsync(IParticle client, ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;

        // ---------------- Step 1: Read message header ----------------
        // The first 8 bytes = topic ID
        // The next 8 bytes = message correlation ID
        ulong topic = Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(span));
        ulong msgId = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(span), 8));

        // The rest of the bytes after the first 16 bytes is the actual command body
        var body = payload.Slice(16);

        // ---------------- Step 2: Find the handler ----------------
        var handler = messageHandler.GetHandler(topic);
        if (handler is null)
        {
            // No handler for this command — send back an empty response
            client.Send(s_emptyResponse);
            return;
        }

        // ---------------- Step 3: Execute the handler ----------------
        // The handler returns a serialized response as a ReadOnlyMemory<byte>
        ReadOnlyMemory<byte> result = await handler
            .Invoke(serviceProvider, commandSerializer, body)
            .ConfigureAwait(false);

        // ---------------- Step 4: Send back the response ----------------
        int totalLen = 8 + result.Length;

        // Get or rent a reusable buffer (thread-local)
        byte[] buffer = s_responseBuffer ??= ArrayPool<byte>.Shared.Rent(8 * 1024);

        // If the buffer isn't big enough, rent a larger one temporarily
        if (buffer.Length < totalLen)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = ArrayPool<byte>.Shared.Rent(totalLen);
            s_responseBuffer = buffer;
        }

        var spanBuf = buffer.AsSpan(0, totalLen);

        // Write the message ID (8 bytes) at the start of the response
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(spanBuf), msgId);

        // Copy the serialized command response (if any) after it
        if (result.Length > 0)
            result.Span.CopyTo(spanBuf.Slice(8));

        // Send the response back to the client
        client.Send(spanBuf.Slice(0, totalLen));
    }
       
    /// <summary>
    /// Disposes of all reactors and their threads safely.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _tcpServer?.Dispose();
            _ipcServer?.Dispose();
            _inprocServer?.Dispose();
        }
        catch
        {
            // It's safe to ignore shutdown race conditions during disposal
        }

        // Try to gracefully stop threads (non-blocking)
        _tcpThread?.Join(50);
        _ipcThread?.Join(50);

        // Return any thread-local buffer to the pool
        if (s_responseBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(s_responseBuffer);
            s_responseBuffer = null;
        }
    }
}
