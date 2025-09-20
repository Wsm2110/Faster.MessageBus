using Faster.MessageBus.Features.Commands;
using Faster.MessageBus.Features.Events.Shared;
using NetMQ;

namespace Faster.MessageBus.Features.Events.Contracts;

public interface IEventScheduler : IDisposable
{
    // Note: While Dispose() is listed, it's best practice to formally inherit from IDisposable
    // to make the contract explicit to the compiler and other developers.

    /// <summary>
    /// Schedules a command action to be executed by the scheduler.
    /// </summary>
    /// <param name="action">The <see cref="ScheduleCommand"/> delegate to invoke.</param>
    void Invoke(ScheduleEvent scheduleEvent);

    /// <summary>
    /// Schedules a generic action to be executed by the scheduler.
    /// </summary>
    /// <param name="action">The <see cref="Action"/> delegate to invoke.</param>
    void Invoke(Action<NetMQPoller> action);
}