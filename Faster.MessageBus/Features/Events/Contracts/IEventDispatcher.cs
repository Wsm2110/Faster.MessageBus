using Faster.MessageBus.Contracts;

namespace Faster.MessageBus.Features.Events.Contracts
{
    public interface IEventDispatcher
    {
        Task PublishAsync(IEvent @event);
        void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class, IEvent;
    }
}