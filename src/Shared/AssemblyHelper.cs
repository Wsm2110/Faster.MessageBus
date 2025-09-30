using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Shared;

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyModel;

public static class AssemblyHelper
{
    // 1. Static field to store the loaded assemblies.
    // Initialization occurs the first time the field is accessed.
    private static readonly Assembly[] _applicationAssemblies = LoadApplicationAssembliesInternal();

    /// <summary>
    /// Gets the array of application-specific and referenced assemblies, 
    /// ensuring the loading process only executes once.
    /// </summary>
    public static Assembly[] ApplicationAssemblies => _applicationAssemblies;

    /// <summary>
    /// Loads the assemblies. This method is called exactly once during static initialization.
    /// </summary>
    /// <returns>An array of successfully loaded unique assemblies.</returns>
    private static Assembly[] LoadApplicationAssembliesInternal()
    {
        // Check if DependencyContext is available (primarily for .NET Core/.NET 5+)
        if (DependencyContext.Default == null)
        {
            Console.WriteLine("Warning: DependencyContext.Default is null. Returning only the executing assembly.");
            return new[] { Assembly.GetExecutingAssembly() };
        }

        var assemblies = DependencyContext.Default.RuntimeLibraries
            // Filter to find libraries that are part of your application (not Microsoft/System infrastructure).
            .Where(lib => !lib.Name.StartsWith("Microsoft.") && !lib.Name.StartsWith("System."))
            .Select(lib =>
            {
                try
                {
                    // Attempt to load the assembly
                    return Assembly.Load(new AssemblyName(lib.Name));
                }
                catch (Exception ex)
                {
                    // Log the failure to load the assembly
                    Console.WriteLine($"Warning: Could not load assembly '{lib.Name}'. Error: {ex.Message}");
                    return null; // Return null for failed loads
                }
            })
            .Where(assembly => assembly != null) // Remove nulls (failed loads)
            .Append(Assembly.GetExecutingAssembly()) // Add the current executing assembly (self)
            .Distinct() // Ensure all assemblies are unique
            .ToArray();

        return assemblies;
    }
}
