namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Interface for AirApp desktop component widgets.
/// </summary>
public interface IAirAppWidget
{
    /// <summary>
    /// Gets or sets the component context.
    /// Set by the host when the widget is created.
    /// </summary>
    IAirAppComponentContext Context { get; set; }

    /// <summary>
    /// Called when the widget is attached to the desktop.
    /// </summary>
    void OnAttached();

    /// <summary>
    /// Called when the widget is detached from the desktop.
    /// </summary>
    void OnDetached();

    /// <summary>
    /// Called when the appearance (theme) has changed.
    /// </summary>
    /// <param name="snapshot">New appearance snapshot</param>
    void OnAppearanceChanged(AirAppAppearanceSnapshot snapshot);
}
