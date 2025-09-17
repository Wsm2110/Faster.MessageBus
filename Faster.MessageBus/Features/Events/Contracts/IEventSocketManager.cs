using Faster.MessageBus.Shared;
using NetMQ.Sockets;

namespace Faster.MessageBus.Features.Events.Contracts;

    public interface IEventSocketManager
    {

        void AddSocket(MeshInfo info);
        void Dispose();
        PublisherSocket PublisherSocket { get; set; }
        void RemoveSocket(MeshInfo meshInfo);
    }
