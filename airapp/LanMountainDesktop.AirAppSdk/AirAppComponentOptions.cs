namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Describes a desktop component contributed by an AirApp.
/// </summary>
/// <remarks>
/// The host has no separate "default size" concept: a component is placed at
/// <see cref="MinWidthCells"/> x <see cref="MinHeightCells"/> unless the user resizes it,
/// so these two values double as the component's initial size on the desktop grid.
/// </remarks>
public sealed class AirAppComponentOptions
{
    public required string ComponentId { get; set; }

    public required string DisplayName { get; set; }

    public string IconKey { get; set; } = "PuzzlePiece";

    public string Category { get; set; } = "AirApps";

    public int MinWidthCells { get; set; } = 2;

    public int MinHeightCells { get; set; } = 2;

    public bool AllowDesktopPlacement { get; set; } = true;

    public bool AllowStatusBarPlacement { get; set; }

    public AirAppComponentResizeMode ResizeMode { get; set; } = AirAppComponentResizeMode.Proportional;

    public string? DisplayNameLocalizationKey { get; set; }

    public string? Description { get; set; }

    public string? DescriptionLocalizationKey { get; set; }

    public AirAppCornerRadiusPreset CornerRadiusPreset { get; set; } = AirAppCornerRadiusPreset.Default;

    public Func<IAirAppAppearanceContext, double, double>? CornerRadiusResolver { get; set; }
}
