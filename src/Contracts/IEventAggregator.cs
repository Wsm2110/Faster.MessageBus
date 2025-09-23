namespace Faster.MessageBus.Contracts
{
    public interface IEventAggregator : IDisposable
    {
        void Publish<TEvent>(TEvent e);
        void Subscribe<TEvent>(Action<TEvent> handler);
        void Unsubscribe<TEvent>(Action<TEvent> handler);
    }
}