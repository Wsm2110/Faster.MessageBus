using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Contracts;

/// <summary>
/// Defines a handler for a message
/// </summary>
/// <typeparam name="TRequest">The type of message being handled</typeparam>
/// <typeparam name="TResponse">The type of response from the handler</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    /// <summary>
    /// Handles a message
    /// </summary>
    /// <param name="message">The message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from the message</returns>
    Task<TResponse> Handle(TCommand message, CancellationToken cancellationToken);
}

/// <summary>
/// Defines a handler for a message with a void response.
/// </summary>
/// <typeparam name="TRequest">The typeTRequest of message being handled</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>
    /// Handles a message
    /// </summary>
    /// <param name="message">The message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from the message</returns>
    Task Handle(TCommand message, CancellationToken cancellationToken);
}