using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Contracts 
{
    /// <summary>
    /// Marker interface to represent a command message that **does not return a value** (a "void" response).
    /// </summary>
    /// <remarks>
    /// This is the base interface for all commands and is used for fire-and-forget operations.
    /// </remarks>
    public interface ICommand { }

    /// <summary>
    /// Marker interface to represent a command message that **expects a response**.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response expected after the command is processed.</typeparam>
    /// <remarks>
    /// This interface extends <see cref="ICommand"/> and is typically used for synchronous
    /// request/response patterns (often called a "Query" in CQRS).
    /// </remarks>
    public interface ICommand<out TResponse> : ICommand
    {
        // No members are defined. This interface is purely for type identification.
    }
}