using Microsoft.Extensions.DependencyInjection;

namespace Faster.MessageBus.Contracts;

/// <summary>
/// Defines a contract for a service installer, which encapsulates the dependency injection (IoC) registrations for a specific feature or module.
/// </summary>
/// <remarks>
/// This interface promotes a modular design by allowing the DI configuration for a feature to be co-located
/// with its implementation. Instead of having a monolithic setup in <c>Program.cs</c>, an application can
/// discover and run all implementations of this interface at startup.
/// <example>
/// Example of discovering and running installers:
/// <code>
/// public static class ServiceInstallerExtensions
/// {
///     public static void InstallServicesInAssembly(this IServiceCollection services, IConfiguration configuration)
///     {
///         var installers = typeof(Program).Assembly.ExportedTypes
///             .Where(x => typeof(IServiceInstaller).IsAssignableFrom(x) &amp;&amp; !x.IsInterface &amp;&amp; !x.IsAbstract)
///             .Select(Activator.CreateInstance)
///             .Cast&lt;IServiceInstaller&gt;()
///             .ToList();
///
///         installers.ForEach(installer => installer.Install(services, configuration));
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public interface IServiceInstaller
{
    /// <summary>
    /// Registers the services and dependencies for a specific feature with the application's service collection.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add the service registrations to.</param>
    void Install(IServiceCollection serviceCollection);
}