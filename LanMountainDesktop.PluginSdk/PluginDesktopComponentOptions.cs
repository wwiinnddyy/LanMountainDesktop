namespace LanMountainDesktop.PluginSdk;

public sealed class PluginDesktopComponentOptions
{
    public required string ComponentId { get; init; }

    public required string DisplayName { get; init; }

    public string IconKey { get; init; } = "PuzzlePiece";

    public string Category { get; init; } = "Plugins";

    public int MinWidthCells { get; init; } = 2;

    public int MinHeightCells { get; init; } = 2;

    public bool AllowDesktopPlacement { get; init; } = true;

    public bool AllowStatusBarPlacement { get; init; }

    public PluginDesktopComponentResizeMode ResizeMode { get; init; } = PluginDesktopComponentResizeMode.Proportional;

    public string? DisplayNameLocalizationKey { get; init; }

    public PluginCornerRadiusPreset CornerRadiusPreset { get; init; } = PluginCornerRadiusPreset.Default;

    public Func<IPluginAppearanceContext, double, double>? CornerRadiusResolver { get; init; }
}
