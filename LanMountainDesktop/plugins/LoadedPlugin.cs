using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.PluginSdk;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.Plugins;

public sealed class LoadedPlugin : IDisposable, IAsyncDisposable
{
    private int _disposed;

    internal LoadedPlugin(
        PluginManifest manifest,
        string sourcePath,
        string assemblyPath,
        Assembly assembly,
        IPlugin plugin,
        IPluginRuntimeContext runtimeContext,
        IServiceProvider services,
        IReadOnlyList<PluginSettingsSectionRegistration> settingsSections,
        IReadOnlyList<PluginDesktopComponentRegistration> desktopComponents,
        IReadOnlyList<PluginServiceExportDescriptor> exportedServices,
        IReadOnlyList<IHostedService> hostedServices,
        PluginLoadContext loadContext)
    {
        Manifest = manifest;
        SourcePath = sourcePath;
        AssemblyPath = assemblyPath;
        Assembly = assembly;
        Plugin = plugin;
        RuntimeContext = runtimeContext;
        Services = services;
        SettingsSections = settingsSections;
        DesktopComponents = desktopComponents;
        ExportedServices = exportedServices;
        HostedServices = hostedServices;
        LoadContext = loadContext;
    }

    public PluginManifest Manifest { get; }

    public string SourcePath { get; }

    public string AssemblyPath { get; }

    public Assembly Assembly { get; }

    public IPlugin Plugin { get; }

    public IPluginRuntimeContext RuntimeContext { get; }

    public IPluginRuntimeContext Context => RuntimeContext;

    public IServiceProvider Services { get; }

    public IReadOnlyList<PluginSettingsSectionRegistration> SettingsSections { get; }

    public IReadOnlyList<PluginDesktopComponentRegistration> DesktopComponents { get; }

    public IReadOnlyList<PluginServiceExportDescriptor> ExportedServices { get; }

    public PluginLoadContext LoadContext { get; }

    private IReadOnlyList<IHostedService> HostedServices { get; }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        for (var i = HostedServices.Count - 1; i >= 0; i--)
        {
            try
            {
                await HostedServices[i].StopAsync(CancellationToken.None);
            }
            catch
            {
                // Ignore plugin hosted service shutdown failures to allow unload cleanup.
            }
        }

        if (Plugin is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (Plugin is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (Services is IAsyncDisposable asyncServices)
        {
            await asyncServices.DisposeAsync();
        }
        else if (Services is IDisposable disposableServices)
        {
            disposableServices.Dispose();
        }

        if (RuntimeContext is IAsyncDisposable asyncContext)
        {
            await asyncContext.DisposeAsync();
        }
        else if (RuntimeContext is IDisposable disposableContext)
        {
            disposableContext.Dispose();
        }

        LoadContext.Unload();
        GC.SuppressFinalize(this);
    }
}
