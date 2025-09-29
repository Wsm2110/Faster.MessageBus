using Faster.MessageBus;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Commands.Scope.Cluster;
using Faster.MessageBus.Features.Commands.Shared;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dispatches commands to different scopes (Local, Machine, Cluster, Network).
/// Each distributed scope runs with its own DI lifetime and dedicated scheduler.
/// </summary>
public class CommandDispatcher : ICommandDispatcher, IDisposable
{
    private readonly IServiceScope _localScope;
    private readonly IServiceScope _machineScope;
    private readonly IServiceScope _clusterScope;
    private readonly IServiceScope _networkScope;

    private bool _disposed;

    /// <summary>
    /// Gets the command scope for high-performance, in-process communication.
    /// This does not require its own DI scope or scheduler.
    /// </summary>
    public ICommandScope Local { get; }

    /// <summary>
    /// Gets the command scope for inter-process communication on the same machine.
    /// Runs in its own scheduler via a dedicated DI scope.
    /// </summary>
    public ICommandScope Machine { get; }

    /// <summary>
    /// Gets the command scope for communication within the local cluster.
    /// Runs in its own scheduler via a dedicated DI scope.
    /// </summary>
    public ICommandScope Cluster { get; }

    /// <summary>
    /// Gets the command scope for communication across a wide area network (WAN).
    /// Runs in its own scheduler via a dedicated DI scope.
    /// </summary>
    public ICommandScope Network { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDispatcher"/> class.
    /// Sets up command scopes for Local, Machine, Cluster, and Network communication.
    /// Each distributed scope (Machine, Cluster, Network) is created in its own DI scope
    /// to allow independent schedulers and resource management.
    /// Ensures the startup sequence is executed before dispatching commands.
    /// </summary>
    /// <param name="provider">The root <see cref="IServiceProvider"/> for dependency resolution.</param>
    public CommandDispatcher(IServiceProvider provider)
    {
        // Resolve the local command scope for in-process communication
        (_localScope, Local) = CreateLocalScope(provider);

        // Create the machine scope for inter-process communication on the same machine
        (_machineScope, Machine) = CreateMachineScope(provider);

        // Create the cluster scope for communication within the local cluster
        (_clusterScope, Cluster) = CreateClusterScope(provider);

        // Create the network scope for WAN communication
        (_networkScope, Network) = CreateNetworkScope(provider);    
    }

    /// <summary>
    /// Creates the machine scope and wires its scheduler and socket strategy.
    /// </summary>
    private static (IServiceScope scope, ICommandScope commandScope) CreateLocalScope(IServiceProvider provider)
    {
        var scope = provider.CreateScope();
        var commandScope = scope.ServiceProvider.GetRequiredService<ICommandScope>();
        var processor = scope.ServiceProvider.GetRequiredService<ICommandProcessor>();
        processor.AddSocketStrategy(new AddLocalSocketStrategy());

        return (scope, commandScope);
    }

    /// <summary>
    /// Creates the machine scope and wires its scheduler and socket strategy.
    /// </summary>
    private static (IServiceScope scope, ICommandScope commandScope) CreateMachineScope(IServiceProvider provider)
    {
        var scope = provider.CreateScope();
        var commandScope = scope.ServiceProvider.GetRequiredService<ICommandScope>();
        var processor = scope.ServiceProvider.GetRequiredService<ICommandProcessor>();
        processor.AddSocketStrategy(new AddMachineSocketStrategy());

        return (scope, commandScope);
    }

    /// <summary>
    /// Creates the cluster scope and wires its scheduler and socket strategy.
    /// </summary>
    private static (IServiceScope scope, ICommandScope commandScope) CreateClusterScope(IServiceProvider provider)
    {
        var scope = provider.CreateScope();
        var commandScope = scope.ServiceProvider.GetRequiredService<ICommandScope>();

        var processor = scope.ServiceProvider.GetRequiredService<ICommandProcessor>();
        processor.AddSocketStrategy(new AddClusterSocketStrategy());

        return (scope, commandScope);
    }

    /// <summary>
    /// Creates the network scope and wires its scheduler and socket strategy.
    /// </summary>
    private static (IServiceScope scope, ICommandScope commandScope) CreateNetworkScope(IServiceProvider provider)
    {
        var scope = provider.CreateScope();
        var commandScope = scope.ServiceProvider.GetRequiredService<ICommandScope>();
        var processor = scope.ServiceProvider.GetRequiredService<ICommandProcessor>();
        // no need for a socket strategy, doesnt have any requirements yet
        return (scope, commandScope);
    }

    /// <summary>
    /// Disposes the dispatcher and releases resources associated with each scope.
    /// This shuts down all schedulers tied to Machine, Cluster, and Network.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose pattern to clean up resources.
    /// </summary>
    /// <param name="disposing">If true, dispose managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _localScope.Dispose();
            _machineScope.Dispose();
            _clusterScope.Dispose();
            _networkScope.Dispose();
        }

        _disposed = true;
    }
}