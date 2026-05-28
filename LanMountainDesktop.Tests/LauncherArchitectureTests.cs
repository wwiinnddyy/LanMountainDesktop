using System.Reflection;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherArchitectureTests
{
    private static readonly string LauncherAssemblyName = "LanMountainDesktop.Launcher";

    [Fact]
    public void Deployment_Update_Startup_Infrastructure_DoNotReferenceAvalonia()
    {
        var forbidden = new[] { "Deployment", "Update", "Startup", "Infrastructure" };
        foreach (var nsSuffix in forbidden)
        {
            var types = GetLauncherTypes($"LanMountainDesktop.Launcher.{nsSuffix}");
            var assembly = types.First().Assembly;
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                a => string.Equals(a.Name, "Avalonia", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void LauncherFlowCoordinator_TypeDoesNotExist()
    {
        var coordinator = typeof(LanMountainDesktop.Launcher.Shell.LauncherOrchestrator).Assembly
            .GetType("LanMountainDesktop.Launcher.Services.LauncherFlowCoordinator", throwOnError: false);
        Assert.Null(coordinator);
    }

    private static IEnumerable<Type> GetLauncherTypes(string namespacePrefix)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, LauncherAssemblyName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Launcher assembly not loaded.");

        return assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal));
    }
}
