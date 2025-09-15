using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Heartbeat.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Faster.MessageBus.Features.Heartbeat.Infrastructure;

/// <summary>
/// Implements <see cref="IServiceInstaller"/> to register services for the node heartbeat and health monitoring feature.
/// </summary>
/// <remarks>
/// This installer registers the <see cref="HeartBeatMonitor"/> as a singleton implementation of <see cref="IHeartBeatMonitor"/>.
/// </redmarks>
public class HeartBeatInstaller : IServiceInstaller
{
    /// <inheritdoc/>
    public void Install(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IHeartBeatMonitor, HeartBeatMonitor>();
    }
}