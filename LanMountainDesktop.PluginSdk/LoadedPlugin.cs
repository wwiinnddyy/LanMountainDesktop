using System.Reflection;
using System.Threading;

namespace LanMountainDesktop.PluginSdk;

public sealed class LoadedPlugin : IDisposable, IAsyncDisposable
{
    private int _disposed;

    internal LoadedPlugin(
        PluginManifest manifest,
        string sourcePath,
        string assemblyPath,
        Assembly assembly,
        IPlugin plugin,
        IPluginContext context,
        IReadOnlyList<PluginSettingsPageRegistration> settingsPages,
        IReadOnlyList<PluginDesktopComponentRegistration> desktopComponents,
        PluginLoadContext loadContext)
    {
        Manifest = manifest;
        SourcePath = sourcePath;
        AssemblyPath = assemblyPath;
        Assembly = assembly;
        Plugin = plugin;
        Context = context;
        SettingsPages = settingsPages;
        DesktopComponents = desktopComponents;
        LoadContext = loadContext;
    }

    public PluginManifest Manifest { get; }

    public string SourcePath { get; }

    public string AssemblyPath { get; }

    public Assembly Assembly { get; }

    public IPlugin Plugin { get; }

    public IPluginContext Context { get; }

    public IReadOnlyList<PluginSettingsPageRegistration> SettingsPages { get; }

    public IReadOnlyList<PluginDesktopComponentRegistration> DesktopComponents { get; }

    public PluginLoadContext LoadContext { get; }

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

        if (Plugin is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (Plugin is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (Context is IAsyncDisposable asyncContext)
        {
            await asyncContext.DisposeAsync();
        }
        else if (Context is IDisposable disposableContext)
        {
            disposableContext.Dispose();
        }

        LoadContext.Unload();
        GC.SuppressFinalize(this);
    }
}
