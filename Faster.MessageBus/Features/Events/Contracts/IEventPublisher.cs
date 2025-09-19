using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Events.Contracts;

/// <summary>
/// Defines a contract for publishing messages over the mesh network.
/// Implementations are responsible for sending topic-based messages using NetMQ PUB sockets.
/// </summary>
internal interface IEventPublisher : IDisposable
{
    /// <summary>
    /// Asynchronously publishes a message where the topic is derived from the message's type name.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to publish.</typeparam>
    /// <param name="msg">The message object to publish.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous publish operation.</returns>
    Task Publish<TMessage>(TMessage msg);

    /// <summary>
    /// Asynchronously publishes a message to a specified topic.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to publish.</typeparam>
    /// <param name="topic">The topic string to publish the message to.</param>
    /// <param name="msg">The message object to publish.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous publish operation.</returns>
    Task Publish<TMessage>(string topic, TMessage msg);
}