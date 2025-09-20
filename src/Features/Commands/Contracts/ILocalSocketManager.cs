using NetMQ.Sockets;

namespace Faster.MessageBus.Features.Commands.Contracts;

/// <summary>
/// Defines a contract for a component that manages and provides access to a single, dedicated local NetMQ <see cref="DealerSocket"/>.
/// </summary>
/// <remarks>
/// This interface is typically used to abstract the creation and configuration of a Socket intended for communication
/// with a service running on the same machine (e.g., connecting to a <c>tcp://localhost</c> endpoint) or within the same process
/// (using an <c>inproc://</c> endpoint).
/// </remarks>
public interface ILocalSocketManager
{
    /// <summary>
    /// Gets the managed local <see cref="DealerSocket"/> instance.
    /// </summary>
    /// <value>
    /// The configured local <see cref="DealerSocket"/>.
    /// </value>
    /// <remarks>
    /// <strong style="color: red;">Warning:</strong> NetMQ sockets are not thread-safe. All operations (send, receive, setting _options)
    /// on this Socket must be performed from the same thread, typically a dedicated network thread running a <c>NetMQPoller</c>.
    /// </remarks>
    DealerSocket LocalSocket { get; }

    void Initialize();
}