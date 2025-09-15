using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Commands.Shared;

public record struct DealerSocketCreated(DealerSocket Socket);
public record struct DealerSocketRemoved(DealerSocket Socket);


