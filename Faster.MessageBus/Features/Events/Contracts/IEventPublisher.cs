using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Events.Contracts;

/// <summary>
/// Interface for publishing messages over the mesh network.
/// Supports topic-based message delivery using NetMQ PUB sockets.
/// </summary>
internal interface IEventPublisher: IDisposable
{
    Task Publish<TMessage>(TMessage msg);
    Task Publish<TMessage>(string topic, TMessage msg);
 
}

