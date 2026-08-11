using Avalonia.Media;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Snapshot of the current appearance settings.
/// </summary>
public sealed class AirAppAppearanceSnapshot
{
    /// <summary>
    /// Gets whether dark mode is enabled.
    /// </summary>
    public bool IsDarkMode { get; init; }

    /// <summary>
    /// Gets the primary accent color.
    /// </summary>
    public Color AccentColor { get; init; }

    /// <summary>
    /// Gets the glass effect opacity (0.0 - 1.0).
    /// </summary>
    public double GlassOpacity { get; init; }

    /// <summary>
    /// Gets the corner radius preset.
    /// </summary>
    public AirAppCornerRadiusPreset CornerRadiusPreset { get; init; }

    /// <summary>
    /// Gets the background color.
    /// </summary>
    public Color BackgroundColor { get; init; }

    /// <summary>
    /// Gets the foreground (text) color.
    /// </summary>
    public Color ForegroundColor { get; init; }

    /// <summary>
    /// Gets the border color.
    /// </summary>
    public Color BorderColor { get; init; }

    /// <summary>
    /// Gets additional custom properties.
    /// </summary>
    public IReadOnlyDictionary<string, object>? CustomProperties { get; init; }
}
