using Avalonia.Controls;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Interface for AirApp windows.
/// </summary>
public interface IAirAppWindow
{
    /// <summary>
    /// Gets the window descriptor (configuration).
    /// </summary>
    AirAppWindowDescriptor Descriptor { get; }

    /// <summary>
    /// Called before the window is opened.
    /// Use this for async initialization.
    /// </summary>
    Task OnWindowOpeningAsync();

    /// <summary>
    /// Called after the window has been opened.
    /// </summary>
    void OnWindowOpened();

    /// <summary>
    /// Called when the window is closing.
    /// Set e.Cancel = true to prevent closing.
    /// </summary>
    void OnWindowClosing(WindowClosingEventArgs e);

    /// <summary>
    /// Called after the window has been closed.
    /// </summary>
    void OnWindowClosed();
}
