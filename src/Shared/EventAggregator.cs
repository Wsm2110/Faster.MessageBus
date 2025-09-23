using Faster.MessageBus.Contracts;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Faster.MessageBus.Shared;

/// <summary>
/// A static event aggregator that provides a thread-safe, async-enabled publish-subscribe mechanism.
/// </summary>
public class EventAggregator : IEventAggregator, IDisposable
{
    // Internal dictionary mapping event types to immutable handler lists
    private readonly ConcurrentDictionary<Type, ImmutableArray<Delegate>> _map = new();

    /// <summary>
    /// Subscribes a handler for a specific event type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Subscribe<TEvent>(Action<TEvent> handler)
    {
        _map.AddOrUpdate(
            typeof(TEvent),
            _ => ImmutableArray.Create<Delegate>(handler),
            (_, existing) => existing.Add(handler)
        );
    }

    /// <summary>
    /// Unsubscribes a handler for a specific event type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        var eventType = typeof(TEvent);

        while (_map.TryGetValue(eventType, out var currentHandlers))
        {
            var newHandlers = currentHandlers.Remove(handler);

            if (newHandlers.Length == currentHandlers.Length)
                break;

            if (newHandlers.IsEmpty)
            {
                if (((ICollection<KeyValuePair<Type, ImmutableArray<Delegate>>>)_map)
                    .Remove(new KeyValuePair<Type, ImmutableArray<Delegate>>(eventType, currentHandlers)))
                    break;
            }
            else
            {
                if (_map.TryUpdate(eventType, newHandlers, currentHandlers))
                    break;
            }
        }
    }

    /// <summary>
    /// Publishes an event to all subscribers of the event type, executing each handler asynchronously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Publish<TEvent>(TEvent e)
    {
        if (_map.TryGetValue(typeof(TEvent), out var delegates))
        {
            foreach (var d in delegates)
            {
                if (d is Action<TEvent> action)
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            action(e);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"EventAggregator handler for {typeof(TEvent).Name} failed: {ex.Message}");
                        }
                    });
                }
            }
        }
    }

    /// <summary>
    /// Clears all subscriptions from the aggregator.
    /// </summary>
    public void Dispose()
    {
        _map.Clear();
    }
}
