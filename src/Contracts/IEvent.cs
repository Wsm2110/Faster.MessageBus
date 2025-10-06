using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Contracts;

/// <summary>
/// Represents a marker interface for an **event** that can be published or handled by the message bus.
/// </summary>
/// <remarks>
/// This interface is typically used to ensure that all classes intended as messages (events)
/// within the system conform to a standard type, allowing for easy identification and processing
/// by the message bus infrastructure. It does not contain any members.
/// </remarks>
public interface IEvent 
{
    // No members are currently defined. This is a marker interface.
}
