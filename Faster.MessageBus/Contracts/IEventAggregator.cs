namespace Faster.MessageBus.Contracts
{
    public interface IEventAggregator
    {
        void Publish<TEvent>(TEvent e);
        void Subscribe<TEvent>(Action<TEvent> handler);
        void Unsubscribe<TEvent>(Action<TEvent> handler);
    }
}