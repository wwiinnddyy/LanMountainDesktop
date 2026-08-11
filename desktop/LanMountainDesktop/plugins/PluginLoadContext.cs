using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Plugins;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly HashSet<string> _sharedAssemblyNames;

    public PluginLoadContext(string mainAssemblyPath, IEnumerable<string>? sharedAssemblyNames = null)
        : base($"{Path.GetFileNameWithoutExtension(mainAssemblyPath)}_{Guid.NewGuid():N}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mainAssemblyPath);

        MainAssemblyPath = Path.GetFullPath(mainAssemblyPath);
        _resolver = new AssemblyDependencyResolver(MainAssemblyPath);
        _sharedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(IPlugin).Assembly.GetName().Name!
        };

        if (sharedAssemblyNames is null)
        {
            return;
        }

        foreach (var assemblyName in sharedAssemblyNames)
        {
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                _sharedAssemblyNames.Add(assemblyName.Trim());
            }
        }
    }

    public string MainAssemblyPath { get; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var simpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(simpleName))
        {
            return null;
        }

        if (_sharedAssemblyNames.Contains(simpleName))
        {
            return Default.Assemblies.FirstOrDefault(
                    assembly => string.Equals(
                        assembly.GetName().Name,
                        simpleName,
                        StringComparison.OrdinalIgnoreCase))
                ?? null;
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null ? nint.Zero : LoadUnmanagedDllFromPath(libraryPath);
    }
}
