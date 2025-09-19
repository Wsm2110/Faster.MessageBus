using NetMQ;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Events.Contracts
{
    /// <summary>
    /// Defines a contract for a handler that processes messages received from a NetMQ socket.
    /// </summary>
    public interface IEventReceivedHandler
    {
        /// <summary>
        /// The callback method executed by a NetMQ poller when a socket has an event, such as a message ready to be received.
        /// </summary>
        /// <param name="sender">The source of the event, typically the <see cref="NetMQPoller"/>.</param>
        /// <param name="e">The event arguments, which include the <see cref="NetMQ.NetMQSocket"/> that is ready for processing.</param>
        void OnEventReceived(object? sender, NetMQSocketEventArgs e);
    }
}