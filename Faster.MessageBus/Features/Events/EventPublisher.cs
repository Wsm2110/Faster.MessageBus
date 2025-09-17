using System.Buffers;
using MessagePack;
using NetMQ;
using NetMQ.Sockets;
using Faster.MessageBus.Shared;
using Faster.MessageBus.Features.Events.Contracts;

namespace Faster.MessageBus.Features.Events;

internal sealed class EventPublisher : IEventPublisher, IDisposable
{
    private readonly List<PublisherSocket> _pubSockets = new();
    private readonly NetMQQueue<Action> _actionQueue;
    private readonly NetMQPoller _poller;
    private readonly Thread _pollerThread;
    private readonly LocalEndpoint _endpoint;

    // Thread-local _serializer buffer to avoid allocations
    [ThreadStatic] private static ArrayBufferWriter<byte>? _serializerBuffer;

    public EventPublisher(LocalEndpoint endpoint)
    {
        _endpoint = endpoint;

        _actionQueue = new NetMQQueue<Action>();
        _actionQueue.ReceiveReady += (_, _) =>
        {
            while (_actionQueue.TryDequeue(out var action, TimeSpan.Zero))
            {
                action();
            }
        };

        _poller = new NetMQPoller { _actionQueue };
        _pollerThread = new Thread(_poller.Run)
        {
            IsBackground = true,
            Name = "PublisherPoller"
        };
        _pollerThread.Start();

        BindPublisherSocket();
    }

    private void BindPublisherSocket()
    {
        _actionQueue.Enqueue(() =>
        {
            var pubSocket = new PublisherSocket();
            var port = PortFinder.FindAvailablePort(port => pubSocket.Bind($"tcp://*:{port}"));
            _endpoint.PubPort = port;

            _pubSockets.Add(pubSocket);
            _poller.Add(pubSocket);
        });
    }
    public void Dispose() => Stop();

    public void Stop()
    {
        var done = new ManualResetEventSlim();
        _actionQueue.Enqueue(() =>
        {
            foreach (var pubSocket in _pubSockets)
            {
                _poller.Remove(pubSocket);
                pubSocket.Dispose();
            }
            _pubSockets.Clear();
            done.Set();
        });

        done.Wait();
        _poller.Stop();
        _poller.Dispose();
        _actionQueue.Dispose();
        _pollerThread.Join();
    }

    public Task Publish<TMessage>(TMessage msg) => Publish(typeof(TMessage).Name, msg);

    public Task Publish<TMessage>(string topic, TMessage msg)
    {
        // Serialize efficiently into reusable buffer
        var buffer = _serializerBuffer ??= new ArrayBufferWriter<byte>(1024);
        buffer.Clear();
        MessagePackSerializer.Serialize(buffer, msg);

        // Copy to byte[] only once per publish
        var payloadBytes = buffer.WrittenSpan.ToArray();

        _actionQueue.Enqueue(() =>
        {
            if (_pubSockets.Count == 1)
            {
                _pubSockets[0]
                    .SendMoreFrame(topic)
                    .SendFrame(payloadBytes);
            }
            else
            {
                foreach (var pub in _pubSockets)
                    pub.SendMoreFrame(topic).SendFrame(payloadBytes);
            }
        });

        return Task.CompletedTask;
    }
}
