using Faster.MessageBus.Features.Commands.Extensions;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;
using NetMQ;
using NetMQ.Sockets;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// A thread-safe, high-performance command server using a NetMQ Router Socket. It listens for incoming requests,
/// dispatches them to registered command handlers via an <see cref="ICommandServerDispatcher"/>, and sends back responses.
/// The server uses a dedicated poller thread to manage all Socket I/O, ensuring thread safety and high throughput.
/// </summary>
public class CommandServer : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICommandSerializer _commandSerializer;

    /// <summary>
    /// The messageHandler responsible for invoking the correct business logic based on the request topic.
    /// </summary>
    private readonly ICommandMessageHandler _messageHandler;
    private readonly string _serverName;

    /// <summary>
    /// The core NetMQ Socket that listens for client connections and handles asynchronous request-reply patterns.
    /// It automatically manages client identities.
    /// </summary>
    private readonly RouterSocket _router;

    /// <summary>
    /// The poller that runs the event loop on a dedicated thread, monitoring sockets and queues for activity.
    /// </summary>
    private readonly NetMQPoller _poller;

    /// <summary>
    /// A thread-safe queue used to marshal response messages from the business logic thread(s) back to the poller thread for safe sending.
    /// </summary>
    private readonly NetMQQueue<NetMQMessage> _queue;

    /// <summary>
    /// The dedicated background thread that runs the NetMQPoller.
    /// </summary>
    private readonly Thread _pollerThread;

    /// <summary>
    /// A flag to prevent redundant disposal.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// A pre-allocated empty byte array for use as a delimiter frame in NetMQ messages.
    /// </summary>
    private static readonly byte[] EmptyFrame = Array.Empty<byte>();

    /// <summary>
    /// Gets a string representation of a response, potentially for testing or debugging purposes.
    /// </summary>
    public string SendWithValueResponse { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandServer"/> class.
    /// </summary>
    /// <param name="messageHandler">The messageHandler that will handle the business logic for incoming commands.</param>
    /// <param name="localMeshEndpoint">The Local endpoint configuration, including the port to bind the RPC server to.</param>
    public CommandServer(
        IOptions<MessageBrokerOptions> options,
        IServiceProvider serviceProvider,
        ICommandSerializer commandSerializer,
        ICommandMessageHandler messageHandler,
        LocalEndpoint localMeshEndpoint)
    {
        _serviceProvider = serviceProvider;
        _commandSerializer = commandSerializer;
        _messageHandler = messageHandler;
        _serverName = $"server: {options.Value.ApplicationName}";

        // Initialize and configure the Router Socket with performance-oriented _options.
        _router = new RouterSocket();

        // Register the callback for incoming messages on the poller thread.
        _router.ReceiveReady += ReceivedFromDealer!;

        // Set Socket _options for high throughput and reliability.
        _router.Options.Linger = TimeSpan.Zero;             // Don't buffer on close
        _router.Options.SendHighWatermark = 1_000_000;      // Huge outbound queue
        _router.Options.ReceiveHighWatermark = 1_000_000;   // Huge inbound queue
        _router.Options.Backlog = 1024;                     // Enough for bursty connects
        _router.Options.TcpKeepalive = true;
        _router.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(30);
        _router.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(10);
        _router.Options.ReceiveBuffer = 1024 * 1024;        // OS recv buffer size
        _router.Options.SendBuffer = 1024 * 1024;           // OS send buffer size


        // find random port in range of 10000 -12000
        var port = PortFinder.FindAvailablePort(options.Value.RPCPort, port => _router.Bind($"tcp://*:{port}"));
        localMeshEndpoint.RpcPort = port;

        Console.WriteLine(_serverName + $"tcp://*:{port}");

        // Initialize the response queue and its callback.
        _queue = new NetMQQueue<NetMQMessage>();
        _queue.ReceiveReady += SendResponseToDealer;

        // The poller will monitor both the router for new requests and the queue for new responses to send.
        _poller = new NetMQPoller { _router, _queue };
        _poller.RunAsync();
    }

    /// <summary>
    /// Event handler for the response queue. It dequeues and sends messages on the poller's thread,
    /// ensuring all Socket write operations are thread-safe.
    /// </summary>
    private void SendResponseToDealer(object? sender, NetMQQueueEventArgs<NetMQMessage> e)
    {
        // Dequeue and send all available response messages.
        while (_queue.TryDequeue(out var msg, TimeSpan.Zero))
        {
            _router.SendMultipartMessage(msg);
        }
    }

    /// <summary>
    /// Event handler for the Router Socket. It receives incoming messages and offloads them for processing.
    /// This method is executed on the poller's dedicated thread.
    /// </summary>
    /// <remarks>
    /// The expected incoming message format is: [identity][empty][topic][correlationId][payload]
    /// </remarks>
    private void ReceivedFromDealer(object sender, NetMQSocketEventArgs e)
    {
        var msg = new NetMQMessage();
        if (!e.Socket.TryReceiveMultipartMessage(ref msg))
        {
            return;
        }

        // Offload processing to an async method to avoid blocking the poller thread.
        // The fire-and-forget pattern is used here for maximum throughput.
        _ = HandleRequestAsync(msg);
    }

    /// <summary>
    /// Asynchronously processes a single request message.
    /// </summary>
    /// <remarks>
    /// This method parses the request, dispatches it to the business logic handler,
    /// builds the response message, and queues it for sending on the poller thread.
    /// </remarks>
    /// <param name="msg">The incoming NetMQ message to process.</param>
    private async ValueTask HandleRequestAsync(NetMQMessage msg)
    {
        // Parse the incoming message frames without copying where possible.
        var identity = msg[0];

        var topic = FastConvert.BytesToUlong(msg[2].Buffer);
        var correlationId = msg[3];
        var payloadFrame = msg[4];

        // Dispatch the payload to the appropriate command handler.
        var payload = new ReadOnlySequence<byte>(payloadFrame.Buffer, 0, payloadFrame.MessageSize);

        var result = await _messageHandler.GetHandler(topic).Invoke(_serviceProvider, _commandSerializer, payload);

        // Build the response message.
        msg.Clear();
        msg.Append(identity);
        msg.AppendEmptyFrame();
        msg.Append(correlationId);
        msg.Append(result);

        // Enqueue the response to be sent safely on the poller thread.
        _queue.Enqueue(msg);
    }

    /// <summary>
    /// Stops the poller, closes the Socket, and disposes all managed resources cleanly.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // The shutdown sequence must be carefully managed.
        // Stopping the poller will exit the thread's run loop.
        _poller.Stop();

        // Wait for the poller thread to finish execution.
        _pollerThread.Join();

        // Now that the thread is stopped, it's safe to dispose of NetMQ resources.
        _poller.Dispose();
        _queue.Dispose();
        _router.Dispose();
    }
}