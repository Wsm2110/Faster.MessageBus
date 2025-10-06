﻿using Faster.MessageBus.Features.Commands.Shared;

namespace Faster.MessageBus.Shared;

/// <summary>
/// Provides configuration options for the Faster.MessageBus message broker.
/// </summary>
public class MessageBrokerOptions
{
    public bool? UseSameMachineOptimization { get; set; } = false;

    public bool AutoScan { get; set; } = true;

    /// <summary>
    /// Gets or sets the name of the application. This can be used for identification, logging, or prefixing topics.
    /// </summary>
    public string ApplicationName { get; set; } = WyRandom.Shared.NextInt64().ToString();

    /// <summary>
    /// Gets or sets the network _port used for RPC (Request-Reply) communication.
    /// </summary>
    public ushort RPCPort { get; set; } = 20000;

    /// <summary>
    /// Gets or sets the network _port used for Publish-Subscribe communication.
    /// </summary>
    public ushort PublishPort { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the cluster configuration, defining how nodes discover and connect to each other.
    /// </summary>
    public Cluster Cluster { get; set; } = new Cluster();

    /// <summary>
    /// Gets or sets the default timeout for message operations, such as request-reply calls. The default is 1 second.
    /// </summary>
    public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromSeconds(1); 
    public TimeSpan CleanupInterval { get; internal set; } = TimeSpan.FromSeconds(20);
    public TimeSpan BeaconInterval { get; internal set; } = TimeSpan.FromSeconds(1);
    public TimeSpan InactiveThreshold { get; internal set; } = TimeSpan.FromSeconds(10);
}