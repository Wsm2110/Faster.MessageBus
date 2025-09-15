using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Events.Contracts;

/// <summary>
/// Defines the contract for discovering consumer types.
/// </summary>
internal interface IEventHandlerAssemblyScanner
{
    /// <summary>
    /// Discovers consumer types that implement INotifcation<TEvent> and returns a list of them.
    /// </summary>
    /// <returns>A list of tuples containing the discovered message types and their corresponding consumer types.</returns>
    IEnumerable<(Type consumerImpl, Type messageType)> FindEventTypes();
}

