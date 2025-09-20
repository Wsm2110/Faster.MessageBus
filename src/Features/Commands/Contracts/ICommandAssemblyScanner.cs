using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Commands.Contracts;


/// <summary>
/// Defines the contract for discovering message _replyHandler types.
/// </summary>
public interface ICommandAssemblyScanner
{
    /// <summary>
    /// Discovers message _replyHandler types that implement IRequestHandlerProvider<TNotification, TResponse> and returns a list of them.
    /// </summary>
    /// <returns>A list of tuples containing the message type and response type for each discovered _replyHandler.</returns>
    IEnumerable<(Type messageType, Type responseType)> FindAllCommands();      
}
