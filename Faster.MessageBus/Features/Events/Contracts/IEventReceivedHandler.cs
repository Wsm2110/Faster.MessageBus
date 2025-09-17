using NetMQ;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Events.Contracts
{
    public interface IEventReceivedHandler
    {
        void OnEventReceived(object? sender, NetMQSocketEventArgs e);
    }
}
