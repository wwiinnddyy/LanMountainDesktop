using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.AirAppSdk;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.AirApps;

public sealed class LoadedAirApp : IDisposable, IAsyncDisposable
{
    private int _disposed;

    internal LoadedAirApp(
        AirAppManifest manifest,
        string sourcePath,
        string assemblyPath,
        Assembly assembly,
        IAirApp plugin,
        IAirAppRuntimeContext runtimeContext,
        IServiceProvider services,
        IReadOnlyList<AirAppSettingsSectionRegistration> settingsSections,
        IReadOnlyList<AirAppComponentRegistration> desktopComponents,
        IReadOnlyList<AirAppComponentEditorRegistration> desktopComponentEditors,
        IReadOnlyList<AirAppServiceExportDescriptor> exportedServices,
        IReadOnlyList<AirAppPublicIpcServiceDescriptor> publicIpcServices,
        IReadOnlyList<IHostedService> hostedServices,
        AirAppLoadContext loadContext)
    {
        Manifest = manifest;
        SourcePath = sourcePath;
        AssemblyPath = assemblyPath;
        Assembly = assembly;
        AirApp = plugin;
        RuntimeContext = runtimeContext;
        Services = services;
        SettingsSections = settingsSections;
        DesktopComponents = desktopComponents;
        DesktopComponentEditors = desktopComponentEditors;
        ExportedServices = exportedServices;
        PublicIpcServices = publicIpcServices;
        HostedServices = hostedServices;
        LoadContext = loadContext;
    }

    public AirAppManifest Manifest { get; }

    public string SourcePath { get; }

    public string AssemblyPath { get; }

    public Assembly Assembly { get; }

    public IAirApp AirApp { get; }

    public IAirAppRuntimeContext RuntimeContext { get; }

    public IAirAppRuntimeContext Context => RuntimeContext;

    public IServiceProvider Services { get; }

    public IReadOnlyList<AirAppSettingsSectionRegistration> SettingsSections { get; }

    public IReadOnlyList<AirAppComponentRegistration> DesktopComponents { get; }

    public IReadOnlyList<AirAppComponentEditorRegistration> DesktopComponentEditors { get; }

    public IReadOnlyList<AirAppServiceExportDescriptor> ExportedServices { get; }

    public IReadOnlyList<AirAppPublicIpcServiceDescriptor> PublicIpcServices { get; }

    public AirAppLoadContext LoadContext { get; }

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

        try
        {
            await AirApp.OnStoppingAsync();
        }
        catch
        {
            // Ignore AirApp stopping failures to allow unload cleanup.
        }

        if (AirApp is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }        else if (AirApp is IDisposable disposable)
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
