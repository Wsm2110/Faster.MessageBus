using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Commands.Shared;

public class CommandProcessingException : Exception
{
    public CommandProcessingException(string message) : base(message) { }
}

