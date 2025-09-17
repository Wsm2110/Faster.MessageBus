using Faster.MessageBus.Contracts;

namespace Faster.MessageBus.Features.Events.Contracts
{
    public interface IEventDispatcher
    {
        void Publish(IEvent @event);     
    }
}