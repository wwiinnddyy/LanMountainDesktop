namespace LanMountainDesktop.PluginSdk;

public sealed record InstalledPluginInfo(
    PluginManifest Manifest,
    bool IsEnabled,
    bool IsLoaded,
    bool IsPackage,
    string? ErrorMessage);
