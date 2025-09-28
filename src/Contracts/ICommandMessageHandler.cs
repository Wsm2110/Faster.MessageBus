using Faster.MessageBus.Features.Commands;
using System.Buffers;

namespace Faster.MessageBus.Contracts
{
    /// <summary>
    /// Defines a contract for a service that manages the registration, initialization, 
    /// and retrieval of command handlers.
    /// </summary>
    public interface ICommandMessageHandler
    {
            
        /// <summary>
        /// Retrieves the executable handler delegate for a specific command topic.
        /// </summary>
        /// <param name="topic">The unique identifier (hash) of the command handler to retrieve.</param>
        /// <returns>
        /// A function delegate that encapsulates the logic to deserialize the command, execute the handler,
        /// and serialize the response. This may throw an exception if no handler is found for the topic.
        /// </returns>
        Func<IServiceProvider, ICommandSerializer, ReadOnlySequence<byte>, Task<byte[]>> GetHandler(ulong topic);

        /// <summary>
        /// Initializes the message handler by scanning and registering all command types from a given collection.
        /// This is the primary method for populating the handler with commands discovered at startup.
        /// </summary>
        /// <param name="commandTypes">
        /// An enumerable of tuples, where each tuple contains the command <see cref="Type"/>
        /// and its corresponding response <see cref="Type"/> (which can be null if there is no response).
        /// </param>
        void Initialize(IEnumerable<(Type messageType, Type responseType)> commandTypes);


    }
}