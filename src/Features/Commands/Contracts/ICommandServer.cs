namespace Faster.MessageBus.Features.Commands.Contracts
{
    /// <summary>
    /// Defines the contract for a command server instance that can be started, scaled, and disposed.
    /// </summary>
    public interface ICommandServer : IDisposable
    {
        /// <summary>
        /// Starts the command server and begins listening for incoming commands.
        /// </summary>
        /// <param name="scaleOut">
        /// Indicates whether the server is being started as part of a scale-out operation.
        /// Default is <c>false</c>, meaning it is the primary instance.
        /// </param>
        void Start(string serverName, bool scaleOut = false);
    }
}
