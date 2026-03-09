namespace LanMountainDesktop.PluginSdk;

public sealed record PluginLoadResult(
    string SourcePath,
    PluginManifest? Manifest,
    LoadedPlugin? LoadedPlugin,
    Exception? Error)
{
    public bool IsSuccess => LoadedPlugin is not null && Error is null;

    public static PluginLoadResult Success(string sourcePath, PluginManifest manifest, LoadedPlugin loadedPlugin)
    {
        return new PluginLoadResult(sourcePath, manifest, loadedPlugin, null);
    }

    public static PluginLoadResult Failure(string sourcePath, PluginManifest? manifest, Exception error)
    {
        return new PluginLoadResult(sourcePath, manifest, null, error);
    }
}
