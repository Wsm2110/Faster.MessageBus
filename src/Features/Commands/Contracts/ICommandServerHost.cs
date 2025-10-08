namespace Faster.MessageBus.Features.Commands.Contracts
{
    /// <summary>
    /// Defines a host that manages the lifecycle of one or more <see cref="ICommandServer"/> instances.
    /// </summary>
    internal interface ICommandServerHost : IDisposable
    {
        /// <summary>
        /// Initializes and starts the primary <see cref="ICommandServer"/> instance.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Scales out by starting additional <see cref="ICommandServer"/> instances
        /// based on the configured scaling options.
        /// </summary>
        void ScaleOut();
    }
}
