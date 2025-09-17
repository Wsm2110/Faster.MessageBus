using NetMQ;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Commands.Contracts;


/// <summary>
/// Defines a contract for a scheduler that queues actions or commands for deferred execution.
/// </summary>
/// <remarks>
/// This interface is typically implemented by a class that manages a dedicated thread or task
/// to process a queue of work items sequentially. This pattern is useful for serializing access
/// to a resource that is not thread-safe, such as a network socket or a file stream,
/// ensuring that all operations are performed in a controlled and orderly fashion.
/// </remarks>
public interface ICommandScheduler : IDisposable
{
    // Note: While Dispose() is listed, it's best practice to formally inherit from IDisposable
    // to make the contract explicit to the compiler and other developers.

    /// <summary>
    /// Schedules a command action to be executed by the scheduler.
    /// </summary>
    /// <param name="action">The <see cref="ScheduleCommand"/> delegate to invoke.</param>
    void Invoke(ScheduleCommand action);

    /// <summary>
    /// Schedules a generic action to be executed by the scheduler.
    /// </summary>
    /// <param name="action">The <see cref="Action"/> delegate to invoke.</param>
    void Invoke(Action<NetMQPoller> action);
}