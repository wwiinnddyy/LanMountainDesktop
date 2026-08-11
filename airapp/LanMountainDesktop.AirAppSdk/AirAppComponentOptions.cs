namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppComponentOptions
{
    public required string ComponentId { get; init; }

    public required string DisplayName { get; init; }

    public string IconKey { get; init; } = "PuzzlePiece";

    public string Category { get; init; } = "AirApps";

    public int MinWidthCells { get; init; } = 2;

    public int MinHeightCells { get; init; } = 2;

    public bool AllowDesktopPlacement { get; init; } = true;

    public bool AllowStatusBarPlacement { get; init; }

    public AirAppComponentResizeMode ResizeMode { get; init; } = AirAppComponentResizeMode.Proportional;

    public string? DisplayNameLocalizationKey { get; init; }

    public string? Description { get; init; }

    public string? DescriptionLocalizationKey { get; init; }

    public AirAppCornerRadiusPreset CornerRadiusPreset { get; init; } = AirAppCornerRadiusPreset.Default;

    public Func<IAirAppAppearanceContext, double, double>? CornerRadiusResolver { get; init; }
}
