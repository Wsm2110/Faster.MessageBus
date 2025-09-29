using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Faster.MessageBus.Features.Commands.Contracts;

/// <summary>
/// Defines a contract for a service that discovers registered command handlers.
/// </summary>
public interface ICommandScanner
{
    /// <summary>
    /// Scans the application's configuration to find all registered command handlers
    /// and their associated command and response types.
    /// </summary>
    /// <returns>
    /// An enumerable collection of tuples, where each tuple contains the command's
    /// message type and its corresponding response type. The response type will be null
    /// for commands that do not return a value.
    /// </returns>
    IEnumerable<(Type messageType, Type responseType)> ScanForCommands();
}