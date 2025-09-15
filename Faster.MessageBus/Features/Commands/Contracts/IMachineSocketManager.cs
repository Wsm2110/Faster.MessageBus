using Faster.MessageBus.Shared;
using NetMQ.Sockets;

namespace Faster.MessageBus.Features.Commands.Contracts
{
    public interface IMachineSocketManager
    {
        IEnumerable<DealerSocket> All { get; }
        int Count { get; }

        void AddSocket(MeshInfo info);
        void Dispose();
        bool RemoveSocket(MeshInfo meshInfo);
    }
}