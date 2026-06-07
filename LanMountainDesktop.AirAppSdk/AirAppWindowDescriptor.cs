namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Window configuration descriptor.
/// </summary>
public sealed class AirAppWindowDescriptor
{
    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    public string Title { get; set; } = "AirApp Window";

    /// <summary>
    /// Gets or sets the initial width.
    /// </summary>
    public double Width { get; set; } = 800;

    /// <summary>
    /// Gets or sets the initial height.
    /// </summary>
    public double Height { get; set; } = 600;

    /// <summary>
    /// Gets or sets the minimum width.
    /// </summary>
    public double MinWidth { get; set; } = 400;

    /// <summary>
    /// Gets or sets the minimum height.
    /// </summary>
    public double MinHeight { get; set; } = 300;

    /// <summary>
    /// Gets or sets the chrome mode.
    /// </summary>
    public AirAppWindowChromeMode ChromeMode { get; set; } = AirAppWindowChromeMode.Standard;

    /// <summary>
    /// Gets or sets whether the window can be resized.
    /// </summary>
    public bool CanResize { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the window shows in the taskbar.
    /// </summary>
    public bool ShowInTaskbar { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the window is modal.
    /// </summary>
    public bool ShowAsDialog { get; set; } = false;
}
