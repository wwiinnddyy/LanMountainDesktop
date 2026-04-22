namespace LanMountainDesktop.PluginIsolation.Contracts;

public sealed record PluginAppearanceSnapshotRequest(string SessionId);

public sealed record PluginAppearanceSnapshot(
    string ThemeVariant,
    string? AccentColor = null,
    double CornerRadiusScale = 1.0,
    IReadOnlyDictionary<string, double>? CornerRadiusTokens = null,
    IReadOnlyDictionary<string, string>? ResourceAliases = null);

public sealed record PluginAppearanceChangedNotification(PluginAppearanceSnapshot Snapshot);
