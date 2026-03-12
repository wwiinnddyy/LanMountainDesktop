namespace LanMountainDesktop.PluginSdk;

public interface IPluginRuntimeContext
{
    PluginManifest Manifest { get; }

    string PluginDirectory { get; }

    string DataDirectory { get; }

    IServiceProvider Services { get; }

    IReadOnlyDictionary<string, object?> Properties { get; }

    T? GetService<T>();

    bool TryGetProperty<T>(string key, out T? value);
}
