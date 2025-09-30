using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Discovery.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Faster.MessageBus.Features.Discovery.Infrastructure;

public class MeshDiscoveryInstaller : IServiceInstaller
{
    public void Install(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IMeshDiscoveryService, MeshDiscoveryService>();
        serviceCollection.AddSingleton<IMeshRepository, MeshRepository>();
    }
}
