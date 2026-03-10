namespace LanMountainDesktop.PluginSdk;

public sealed record PluginPackageInstallResult(
    PluginManifest Manifest,
    bool ReplacedExisting,
    bool RestartRequired);
