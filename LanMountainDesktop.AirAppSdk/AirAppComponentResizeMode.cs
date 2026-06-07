namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Resize mode for AirApp desktop components.
/// </summary>
public enum AirAppComponentResizeMode
{
    /// <summary>
    /// Cannot be resized.
    /// </summary>
    None = 0,

    /// <summary>
    /// Can be resized horizontally only.
    /// </summary>
    Horizontal = 1,

    /// <summary>
    /// Can be resized vertically only.
    /// </summary>
    Vertical = 2,

    /// <summary>
    /// Can be resized in both directions.
    /// </summary>
    Both = 3
}
