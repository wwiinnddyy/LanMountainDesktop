using System.Collections.Generic;

namespace LanMountainDesktop.PluginSdk;

public interface IPluginContext
{
    PluginManifest Manifest { get; }

    string PluginDirectory { get; }

    string DataDirectory { get; }

    IServiceProvider Services { get; }

    IReadOnlyDictionary<string, object?> Properties { get; }

    T? GetService<T>();

    bool TryGetProperty<T>(string key, out T? value);

    void RegisterService<TService>(TService service)
        where TService : class;

    void RegisterSettingsPage(PluginSettingsPageRegistration registration);

    void RegisterDesktopComponent(PluginDesktopComponentRegistration registration);
}
