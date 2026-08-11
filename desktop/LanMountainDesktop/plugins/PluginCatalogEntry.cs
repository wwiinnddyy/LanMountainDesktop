using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public enum PluginCatalogSourceKind
{
    Package = 0,
    Manifest = 1,
    DevPlugin = 2
}

public sealed record PluginCatalogEntry(
    PluginManifest Manifest,
    string SourcePath,
    bool IsPackage,
    bool IsEnabled,
    bool IsLoaded,
    string? ErrorMessage,
    int SettingsPageCount,
    int WidgetCount,
    bool IsDevPlugin = false);
