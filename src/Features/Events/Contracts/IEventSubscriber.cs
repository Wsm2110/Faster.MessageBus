using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Events.Contracts;

/// <summary>
/// Interface for a message subscriber that connects to remote publishers
/// and listens for topic-based messages.
/// </summary>
internal interface IEventSubscriber
{
    /// <summary>
    /// Starts the subscriber and begins listening for incoming messages.
    /// </summary>
    void Start();
}
