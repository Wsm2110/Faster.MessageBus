using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Faster.MessageBus.Shared;

/// <summary>
/// A static event aggregator that provides a thread-safe, async-enabled publish-subscribe mechanism.
/// </summary>
public static class EventAggregator
{
    // Internal dictionary mapping event types to immutable _handler lists
    private static readonly ConcurrentDictionary<Type, ImmutableArray<Delegate>> _map = new();

    /// <summary>
    /// Subscribes a _handler for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="handler">The delegate to invoke when the event is published.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Subscribe<TEvent>(Action<TEvent> handler)
    {
        _map.AddOrUpdate(
            typeof(TEvent),
            _ => ImmutableArray.Create<Delegate>(handler),
            (_, existing) => existing.Add(handler)
        );
    }

    /// <summary>
    /// Publishes an event to all subscribers of the event type, executing each _handler asynchronously.
    /// </summary>
    /// <typeparam name="TEvent">The event type being published.</typeparam>
    /// <param name="e">The event instance.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Publish<TEvent>(TEvent e)
    {
        if (_map.TryGetValue(typeof(TEvent), out var delegates))
        {
            foreach (var d in delegates)
            {
                if (d is Action<TEvent> action)
                {
                    // Dispatch the _handler asynchronously to avoid blocking the publisher
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            action(e);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Handler error: {ex}");
                        }
                    });
                }
            }
        }
    }
}

