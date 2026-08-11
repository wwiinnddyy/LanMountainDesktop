namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Window chrome mode for AirApp windows.
/// </summary>
public enum AirAppWindowChromeMode
{
    /// <summary>
    /// Standard window with title bar and borders.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Borderless window with custom chrome.
    /// </summary>
    Borderless = 1,

    /// <summary>
    /// Full-screen window with no decorations.
    /// </summary>
    FullScreen = 2,

    /// <summary>
    /// Tool window (no taskbar icon, small title bar).
    /// </summary>
    Tool = 3,

    /// <summary>
    /// Background-only (no UI, reserved for future use).
    /// </summary>
    BackgroundOnly = 4
}
