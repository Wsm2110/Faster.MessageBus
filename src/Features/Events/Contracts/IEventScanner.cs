using System;
using System.Collections.Generic;

namespace Faster.MessageBus.Features.Events.Contracts;

/// <summary>
/// Defines a contract for a service that discovers registered event types.
/// </summary>
internal interface IEventScanner
{
    /// <summary>
    /// Scans the application's configuration to find all unique event types
    /// that have registered handlers.
    /// </summary>
    /// <returns>
    /// An enumerable collection of unique event types (e.g., typeof(OrderCreatedEvent))
    /// that have at least one handler registered.
    /// </returns>
    public IEnumerable<Type> ScanForEvents();
}