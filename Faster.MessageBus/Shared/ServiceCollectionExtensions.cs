using Faster.MessageBus.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Faster.MessageBus.Shared;

/// <summary>
/// Extension methods for registering MeshMQ messaging services.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventHandlers(this IServiceCollection services)
    {
        // This scans the assembly for all ICommandHandler types and registers them
        // based on the interfaces they implement.
        services.Scan(scan => scan
            .FromEntryAssembly()
            .AddClasses(classes => classes.AssignableTo(typeof(IEventHandler<>)))
            .AsImplementedInterfaces()
            .WithTransientLifetime() // Or Singleton, Transient, etc.
        );

        return services;
    }

    public static IServiceCollection AddCommandHandlers(this IServiceCollection services)
    {
        // This scans the assembly for all ICommandHandler types and registers them
        // based on the interfaces they implement.
        services.Scan(scan => scan
            .FromEntryAssembly()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)))
            .AsImplementedInterfaces()
            .WithTransientLifetime() // Or Singleton, Transient, etc.

            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)))
            .AsImplementedInterfaces()
            .WithTransientLifetime()

            .AddClasses(classes => classes.AssignableTo(typeof(IValueCommandHandler<,>)))
            .AsImplementedInterfaces()
            .WithTransientLifetime()
        );

        return services;
    }


    /// <summary>
    /// Adds MeshMQ messaging infrastructure with configurable _options and _transport.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Delegate to configure MeshMQ _options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddMessageBus(this IServiceCollection services,
        Action<MessageBrokerOptions> options = default)
    {
        if (options != null)
        {
            services.Configure(options);
        }

        var installers = Assembly.GetAssembly(typeof(MessageBroker))!
            .GetTypes()
            .Where(t => typeof(IServiceInstaller).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
            .Select(Activator.CreateInstance)
            .Cast<IServiceInstaller>();

        foreach (var installer in installers)
        {
            installer.Install(services);
        }

        services.AddCommandHandlers();
        services.AddEventHandlers();
        return services;
    }
}

