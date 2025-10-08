using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands;

/// <summary>
/// Hosts and manages one or more <see cref="ICommandServer"/> instances.
/// Responsible for initializing, scaling, and disposing of all command servers.
/// </summary>
internal class CommandServerHost(
    IServiceProvider serviceProvider,
    IOptions<MessageBrokerOptions> options) : ICommandServerHost, IDisposable
{
    /// <summary>
    /// The collection of running command servers managed by this host.
    /// </summary>
    private readonly List<ICommandServer> _servers = new();

    /// <summary>
    /// Indicates whether the host has been disposed.
    /// </summary>
    private bool disposedValue;

    /// <summary>
    /// Initializes and starts a single <see cref="ICommandServer"/> instance.
    /// This is typically used to start the first server when the host is created.
    /// </summary>
    public void Initialize()
    {
        // Resolve an ICommandServer from DI and start it
        var server = serviceProvider.GetRequiredService<ICommandServer>();
        server.Start($"{options.Value.ApplicationName}-0");

        // Track the running server
        _servers.Add(server);
    }

    /// <summary>
    /// Scales out the number of <see cref="ICommandServer"/> instances
    /// based on the configured <see cref="MessageBrokerOptions.ServerInstances "/>.
    /// Each additional server is started in scale-out mode.
    /// </summary>
    public void ScaleOut()
    {
        // Start at 1 since the first server is already started in Initialize()
        for (int i = 1; i < options.Value.ServerInstances ; i++)
        {
            // Resolve and start a new ICommandServer
            var server = serviceProvider.GetRequiredService<ICommandServer>();

            // Pass flag to indicate scaled-out server
            server.Start(serverName: $"{options.Value.ApplicationName}-{i}", scaleOut: true);

            // Track the running server
            _servers.Add(server);
        }
    }

    /// <summary>
    /// Releases managed resources by stopping and disposing all command servers.
    /// </summary>
    /// <param name="disposing">True if called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // Dispose each managed ICommandServer instance
                foreach (var server in _servers)
                {
                    server.Dispose();
                }
            }

            disposedValue = true;
        }
    }

    /// <summary>
    /// Disposes of the resources used by this <see cref="CommandServerHost"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}