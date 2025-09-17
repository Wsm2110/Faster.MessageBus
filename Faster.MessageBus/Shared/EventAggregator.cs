using Faster.MessageBus.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Faster.MessageBus.Shared;

/// <summary>
/// A static event aggregator that provides a thread-safe, async-enabled publish-subscribe mechanism.
/// </summary>
public class EventAggregator : IEventAggregator
{
    // Internal dictionary mapping event types to immutable handler lists
    private readonly ConcurrentDictionary<Type, ImmutableArray<Delegate>> _map = new();

    /// <summary>
    /// Subscribes a handler for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="handler">The delegate to invoke when the event is published.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public  void Subscribe<TEvent>(Action<TEvent> handler)
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
    /// <typeparam name="TEvent">The event type to unsubscribe from.</typeparam>
    /// <param name="handler">The exact handler instance that was previously subscribed.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public  void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        var eventType = typeof(TEvent);

        // This loop implements optimistic concurrency. It will retry if another thread
        // modifies the handler list for this event type at the same time.
        while (_map.TryGetValue(eventType, out var currentHandlers))
        {
            // Create a new list without the specified handler.
            var newHandlers = currentHandlers.Remove(handler);

            // If the handler wasn't in the list, no change is needed, so we can exit.
            if (newHandlers.Length == currentHandlers.Length)
            {
                break;
            }

            // If the new list of handlers is empty, we should try to remove the event type key entirely.
            if (newHandlers.IsEmpty)
            {
                // Attempt to remove the key, but only if its value is still the `currentHandlers` we started with.
                // We need to cast the dictionary to the non-generic interface to use this specific TryRemove overload.
                if (((ICollection<KeyValuePair<Type, ImmutableArray<Delegate>>>)_map).Remove(
                    new KeyValuePair<Type, ImmutableArray<Delegate>>(eventType, currentHandlers)))
                {
                    break; // Success
                }
                // If it failed, another thread changed it. The loop will retry.
            }
            else
            {
                // Attempt to update the value, but only if it hasn't changed since we read it.
                if (_map.TryUpdate(eventType, newHandlers, currentHandlers))
                {
                    break; // Success
                }
                // If it failed, another thread changed it. The loop will retry.
            }
        }
    }


    /// <summary>
    /// Publishes an event to all subscribers of the event type, executing each handler asynchronously.
    /// </summary>
    /// <typeparam name="TEvent">The event type being published.</typeparam>
    /// <param name="e">The event instance.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Publish<TEvent>(TEvent e)
    {
        if (_map.TryGetValue(typeof(TEvent), out var delegates))
        {
            foreach (var d in delegates)
            {
                if (d is Action<TEvent> action)
                {
                    // Dispatch the handler asynchronously to avoid blocking the publisher
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            action(e);
                        }
                        catch (Exception ex)
                        {
                            // A simple error handling mechanism. Consider replacing with a proper logger.
                            Console.Error.WriteLine($"EventAggregator handler for {typeof(TEvent).Name} failed: {ex.Message}");
                        }
                    });
                }
            }
        }
    }
}