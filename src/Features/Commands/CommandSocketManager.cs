﻿using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Shared;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

/// <summary>
/// A self-contained, high-performance processor that manages a collection of <see cref="DealerSocket"/> instances
/// and executes all operations on a dedicated background thread. It uses a struct-based, zero-allocation command
/// queue for common operations to minimize GC pressure and a dictionary for O(1) socket lookups, ensuring
/// extreme performance and thread safety via the actor model.
/// </summary>
public sealed class CommandSocketManager : ICommandSocketManager, IDisposable
{
    #region Internal Commands & Queues

    private static readonly string s_localMachineName = Environment.MachineName.ToLowerInvariant();
    private static readonly string s_localWorkstationName = System.Environment.GetEnvironmentVariables()["COMPUTERNAME"]?.ToString()?.ToLowerInvariant()
        ?? s_localMachineName;
    /// <summary>
    /// Defines the type of socket operation to be performed by the worker thread.
    /// </summary>
    private enum CommandType { AddSocket, RemoveSocket }

    /// <summary>
    /// A lightweight, non-allocating struct used to command the worker thread for socket operations.
    /// </summary>
    private readonly struct SocketCommand
    {
        public readonly CommandType Type;
        public readonly MeshContext MeshInfo;

        public SocketCommand(CommandType type, MeshContext meshInfo)
        {
            Type = type;
            MeshInfo = meshInfo;
        }
    }

    // The core NetMQ poller that drives the event loop on the worker thread.
    private readonly NetMQPoller _poller = new();
    // The dedicated worker thread that runs the poller's event loop.
    private readonly Thread _pollerThread;
    // A high-performance queue for sending serialized message commands.
    private readonly NetMQQueue<ScheduleCommand> _commandSchedulerQueue = new();
    // A zero-allocation queue for frequent socket management operations like add/remove.
    private readonly NetMQQueue<SocketCommand> _socketManagerQueue = new();
    #endregion

    #region Properties

    public TransportMode Transport { get; set; }

    #endregion

    #region Fields

    private readonly ConcurrentDictionary<ulong, (MeshContext Info, DealerSocket Socket)> _sockets = new();
    private readonly IEventAggregator _eventAggregator;
    private readonly ICommandResponseHandler _handler;
    private readonly IOptions<MessageBrokerOptions> _options;
    private readonly Action<MeshJoined> _onMeshJoined;
    private readonly Action<MeshRemoved> _onMeshRemoved;
    private SocketValidationDelegate? _socketStrategy;
    private bool _disposed;

    #endregion

    /// <summary>
    /// Gets the number of sockets currently being managed by the processor.
    /// </summary>
    public int Count => _sockets.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandSocketManager"/> class.
    /// </summary>
    /// <param name="eventAggregator">The event aggregator for subscribing to mesh lifecycle events.</param>
    /// <param name="commandReplyHandler">The handler for processing replies from sockets.</param>
    /// <param name="options">Configuration options for the message broker.</param>  
    public CommandSocketManager(
        IEventAggregator eventAggregator,
        ICommandResponseHandler commandReplyHandler,
        IOptions<MessageBrokerOptions> options)
    {
        _eventAggregator = eventAggregator;
        _handler = commandReplyHandler;
        _options = options;

        _commandSchedulerQueue.ReceiveReady += OnCommandReceived;
        _socketManagerQueue.ReceiveReady += OnSocketReceived;

        _poller.Add(_commandSchedulerQueue);
        _poller.Add(_socketManagerQueue);

        _pollerThread = new Thread(() =>
        {
            _poller.Run();
        })
        {
            IsBackground = true,
            Name = "CommandProcessorThread",
            Priority = ThreadPriority.Highest
        };

        _pollerThread.Start();

        _onMeshJoined = data => AddSocket(data.Info);
        _onMeshRemoved = data => RemoveSocket(data.Info);

        _eventAggregator.Subscribe(_onMeshJoined);
        _eventAggregator.Subscribe(_onMeshRemoved);
    }

    #region Public API

    /// <summary>
    /// Returns an enumerable collection of managed sockets that are eligible for the given topic, up to the specified count.
    /// </summary>
    /// <param name="count">The maximum number of sockets to return. Enumeration stops once this count is reached.</param>
    /// <param name="topic">The topic hash used to filter sockets based on their command routing table.</param>
    /// <returns>
    /// An <see cref="IEnumerable{T}"/> of tuples:
    /// - <c>Id</c>: The unique mesh ID of the socket.
    /// - <c>Info</c>: The <see cref="MeshContext"/> containing routing and metadata.
    /// - <c>Socket</c>: The actual <see cref="DealerSocket"/> instance associated with the mesh ID.
    /// </returns>
    /// <remarks>
    /// This method filters sockets using the <see cref="CommandRoutingFilter"/> associated with each socket. 
    /// Only sockets whose routing table contains the specified <paramref name="topic"/> are returned.
    /// Enumeration stops as soon as <paramref name="count"/> sockets are yielded, even if more eligible sockets exist.
    /// </remarks>
    public IEnumerable<(ulong Id, (MeshContext Info, DealerSocket Socket))> Get(int count, ulong topic)
    {
        int i = 0;
        foreach (var pair in _sockets)
        {
            if (i++ >= count)
            {
                break; // Stop enumeration once the requested count is reached
            }

            var socketInfo = pair.Value.Info;

            // Only yield sockets that contain the topic in their command routing table
            if (CommandRoutingFilter.TryContains(socketInfo.CommandRoutingTable, topic))
            {
                yield return (pair.Key, (socketInfo, pair.Value.Socket));
            }
            else
            {
                // log?
            }
        }
    }

    /// <summary>
    /// Asynchronously schedules the creation and addition of a new <see cref="DealerSocket"/> for a given mesh node.
    /// The operation is performed on the internal worker thread.
    /// </summary>
    /// <param name="info">The mesh node information used to configure the new socket.</param>
    public void AddSocket(MeshContext info) => _socketManagerQueue.Enqueue(new SocketCommand(CommandType.AddSocket, info));

    /// <summary>
    /// Asynchronously schedules the removal and disposal of the socket for a specified mesh node.
    /// The operation is performed on the internal worker thread.
    /// </summary>
    /// <param name="meshInfo">The mesh node information identifying which socket to remove.</param>
    public void RemoveSocket(MeshContext meshInfo) => _socketManagerQueue.Enqueue(new SocketCommand(CommandType.RemoveSocket, meshInfo));

    /// <summary>
    /// Sets the strategy used to validate whether a socket should be created for a given mesh node.
    /// </summary>
    /// <param name="addMachineSocketStrategy">The validation strategy to apply.</param>
    public void AddSocketValidation(SocketValidationDelegate socketValidationDelegate) => _socketStrategy = socketValidationDelegate;

    /// <summary>
    /// Asynchronously schedules a command to be serialized and sent over a socket.
    /// </summary>
    /// <param name="command">The command containing the socket, topic, correlation ID, and payload to send.</param>
    public void ScheduleCommand(ScheduleCommand command) => _commandSchedulerQueue.Enqueue(command);
    #endregion

    #region Worker Thread Handlers

    /// <summary>
    /// Processes socket management commands (TryAdd/TryRemove) from the socket command queue. Must run on the poller thread.
    /// </summary>
    private void OnSocketReceived(object? sender, NetMQQueueEventArgs<SocketCommand> e)
    {
        while (_socketManagerQueue.TryDequeue(out var command, TimeSpan.Zero))
        {
            switch (command.Type)
            {
                case CommandType.AddSocket:
                    HandleAddSocket(command.MeshInfo);
                    break;
                case CommandType.RemoveSocket:
                    HandleRemoveSocket(command.MeshInfo);
                    break;
            }
        }
    }

    /// <summary>
    /// Processes outbound message commands from the message queue. Must run on the poller thread.
    /// </summary>
    private void OnCommandReceived(object? sender, NetMQQueueEventArgs<ScheduleCommand> e)
    {
        while (_commandSchedulerQueue.TryDequeue(out var command, TimeSpan.Zero))
        {
            Span<byte> topicBuffer = stackalloc byte[8];
            ref ulong topicRef = ref Unsafe.As<byte, ulong>(ref topicBuffer[0]);
            topicRef = command.Topic;

            Span<byte> corrBuffer = stackalloc byte[8];
            ref ulong corrRef = ref Unsafe.As<byte, ulong>(ref corrBuffer[0]);
            corrRef = command.CorrelationId;
                     
            // Send frames using byte arrays         
            command.Socket.SendSpanFrame(topicBuffer, true);
            command.Socket.SendSpanFrame(corrBuffer, true);
            command.Socket.SendSpanFrame(command.Payload.Span);        
        }
    }
    #endregion

    #region Internal Socket Logic (Worker Thread ONLY)

    /// <summary>
    /// Handles the logic for creating and adding a new socket. Must run on the poller thread.
    /// </summary>
    private void HandleAddSocket(MeshContext context)
    {
        if (_poller.IsDisposed)
        {
            return;
        }
        if (_socketStrategy != null && !_socketStrategy.Invoke(context, _options))
        {
            return;
        }

        var socket = new DealerSocket();
        socket.ReceiveReady += _handler.ReceivedFromRouter!;

        setSocketOptions(socket);
        var endpoint = DetermineEndpoint(Transport, context);
        socket.Connect(endpoint);

        _poller.Add(socket);

        _sockets.AddOrUpdate(context.MeshId, (context, socket), (id, data) =>
        {
            data.Socket = socket;
            return data;
        });
    }

    private string DetermineEndpoint(TransportMode transport, MeshContext context)
    {
        // RULE 1: If it's the same process (same MeshId), use INPROC
        if (transport == TransportMode.Inproc)
        {
            var endpoint = $"inproc://{context.ApplicationName}";
            return endpoint;
        }

        // RULE 2: If it's the same machine (workstation/hostname match), use IPC
        if (transport == TransportMode.Ipc)
        {
            var endpoint = GetIpcEndpoint(context);
            return endpoint;
        }

        // RULE 3: Otherwise, use TCP
        var tcpEndpoint = $"tcp://{context.Address}:{context.RpcPort}";
        return tcpEndpoint;
    }

    private void setSocketOptions(DealerSocket dealer)
    {
        dealer.Options.Identity = DealerIdentityGenerator.Create();
        dealer.Options.Linger = TimeSpan.Zero;

        // Massive watermarks - never block under load
        // Default is 1000, we use 5M for market data bursts
        dealer.Options.SendHighWatermark = 5_000_000;
        dealer.Options.ReceiveHighWatermark = 5_000_000;

        // HUGE OS-level buffers - reduces syscall overhead dramatically
        // Default is 8KB, we use 8MB (1000x larger)
        // More data per syscall = fewer context switches
        dealer.Options.SendBuffer = 8_388_608;      // 8MB kernel buffer
        dealer.Options.ReceiveBuffer = 8_388_608;

        // TCP keepalive - detect dead connections quickly
        dealer.Options.TcpKeepalive = true;
        dealer.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(30);
        dealer.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(10);

        // IPv4 only - eliminates IPv6 DNS resolution overhead (~100-500μs)
        dealer.Options.IPv4Only = true;

        // Fast reconnection - critical for exchange disconnections
        dealer.Options.ReconnectInterval = TimeSpan.FromMilliseconds(10);
        dealer.Options.ReconnectIntervalMax = TimeSpan.FromSeconds(5);

    }

    /// <summary>
    /// Handles the logic for removing and disposing of a socket. Must run on the poller thread.
    /// </summary>
    private void HandleRemoveSocket(MeshContext meshInfo)
    {
        if (_sockets.TryRemove(meshInfo.MeshId, out var socketInfo))
        {
            _poller.Remove(socketInfo.Socket);
            CleanupSocket(socketInfo.Socket);
        }
    }

    /// <summary>
    /// Unsubscribes event handlers and disposes of a socket. Must run on the poller thread.
    /// </summary>
    private void CleanupSocket(DealerSocket socket)
    {
        if (socket.IsDisposed) return;
        socket.ReceiveReady -= _handler.ReceivedFromRouter!;
        socket.Dispose();
    }

    /// <summary>
    /// Generates IPC endpoint for same-machine communication.
    /// </summary>
    private static string GetIpcEndpoint(MeshContext context)
    {
        // Use a deterministic endpoint based on application name and mesh ID
        // This ensures consistent endpoint across process restarts
        return $"ipc://{context.ApplicationName}";

        // Linux alternative (uncomment if needed):
        // return $"ipc:///tmp/{context.ApplicationName}-{context.MeshId}.ipc";
    }

    #endregion

    #region Disposal
    /// <summary>
    /// Disposes the processor and all managed resources. This method gracefully shuts down the worker
    /// thread, cleans up all sockets, and disposes of all NetMQ objects.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _eventAggregator.Unsubscribe(_onMeshJoined);
        _eventAggregator.Unsubscribe(_onMeshRemoved);

        if (_poller.IsRunning)
        {
            _poller.StopAsync();
        }

        if (_pollerThread.IsAlive)
        {
            _pollerThread.Join();
        }

        _commandSchedulerQueue.Dispose();
        _socketManagerQueue.Dispose();
        _poller.Dispose();

        foreach (var socketInfo in _sockets.Values)
        {
            CleanupSocket(socketInfo.Socket);
        }
        _sockets.Clear();

        GC.SuppressFinalize(this);
    }
    #endregion
}