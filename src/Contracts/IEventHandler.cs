namespace Faster.MessageBus.Contracts
{ 
    // The previously defined contract for an event handler.
    public interface IEventHandler<TEvent> where TEvent : IEvent
    {
        /// <summary>
        /// Asynchronously processes the specified event message.
        /// </summary>
        /// <param name="msg">The event message instance to be handled.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        /// <example>
        /// This example demonstrates how to implement the interface for a specific event.
        /// <code lang="csharp">
        /// // 1. Define a concrete event class.
        /// public class UserCreatedEvent : IEvent
        /// {
        ///     public Guid UserId { get; set; }
        ///     public string Username { get; set; }
        /// }
        ///
        /// // 2. Implement the handler for that event.
        /// public class UserCreatedHandler : IEventHandler&lt;UserCreatedEvent&gt;
        /// {
        ///     private readonly ILogger _logger;
        ///
        ///     public UserCreatedHandler(ILogger logger)
        ///     {
        ///         _logger = logger;
        ///     }
        ///
        ///     public async Task Handle(UserCreatedEvent msg)
        ///     {
        ///         // Example: Perform asynchronous database work or send an email.
        ///         _logger.LogInformation($"Handling UserCreatedEvent for User ID: {msg.UserId}");
        ///         await Task.Delay(100); // Simulate asynchronous work
        ///         _logger.LogInformation($"Welcome email sent to {msg.Username}.");
        ///     }
        /// }
        /// </code>
        /// </example>
        Task Handle(TEvent msg);
    }
}