using Faster.MessageBus.Features.Commands.Scope.Cluster;
using Faster.MessageBus.Features.Commands.Shared;

namespace Faster.MessageBus.Shared;

/// <summary>
/// Provides configuration options for the Faster.MessageBus message broker.
/// </summary>
public class MessageBrokerOptions
{
    /// <summary>
    /// Gets or sets the name of the application. This can be used for identification, logging, or prefixing topics.
    /// </summary>
    public string ApplicationName { get; set; } = WyHash64.Next().ToString("X");

    /// <summary>
    /// Gets or sets the network port used for RPC (Request-Reply) communication.
    /// </summary>
    public ushort RPCPort { get; set; }

    /// <summary>
    /// Gets or sets the network port used for Publish-Subscribe communication.
    /// </summary>
    public ushort PublishPort { get; set; }

    /// <summary>
    /// Gets or sets the cluster configuration, defining how nodes discover and connect to each other.
    /// </summary>
    public Cluster Cluster { get; set; } = new Cluster();

    /// <summary>
    /// Gets or sets the default timeout for message operations, such as request-reply calls. The default is 1 second.
    /// </summary>
    public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromSeconds(1); // Corrected typo: MesssageTimeout -> MessageTimeout

}