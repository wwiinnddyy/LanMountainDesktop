using Avalonia;
using Avalonia.Controls;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Base class for AirApp windows.
/// </summary>
public abstract class AirAppWindowBase : Window, IAirAppWindow
{
    /// <summary>
    /// Gets the window descriptor.
    /// Override this to customize window configuration.
    /// </summary>
    public virtual AirAppWindowDescriptor Descriptor => new()
    {
        Width = 800,
        Height = 600,
        MinWidth = 400,
        MinHeight = 300,
        ChromeMode = AirAppWindowChromeMode.Standard,
        CanResize = true,
        ShowInTaskbar = true,
        ShowAsDialog = false
    };

    /// <summary>
    /// Initializes a new instance of AirAppWindowBase.
    /// </summary>
    protected AirAppWindowBase()
    {
        ApplyDescriptor(Descriptor);
    }

    /// <summary>
    /// Called before the window is opened.
    /// </summary>
    public virtual Task OnWindowOpeningAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called after the window has been opened.
    /// </summary>
    public virtual void OnWindowOpened()
    {
    }

    /// <summary>
    /// Called when the window is closing.
    /// </summary>
    public virtual void OnWindowClosing(WindowClosingEventArgs e)
    {
    }

    /// <summary>
    /// Called after the window has been closed.
    /// </summary>
    public virtual void OnWindowClosed()
    {
    }

    /// <summary>
    /// Apply the window descriptor configuration.
    /// </summary>
    private void ApplyDescriptor(AirAppWindowDescriptor descriptor)
    {
        Width = descriptor.Width;
        Height = descriptor.Height;
        MinWidth = descriptor.MinWidth;
        MinHeight = descriptor.MinHeight;
        CanResize = descriptor.CanResize;
        ShowInTaskbar = descriptor.ShowInTaskbar;
        ShowAsDialog = descriptor.ShowAsDialog;

        // Apply chrome mode
        switch (descriptor.ChromeMode)
        {
            case AirAppWindowChromeMode.Standard:
                SystemDecorations = SystemDecorations.Full;
                break;
            case AirAppWindowChromeMode.Borderless:
                SystemDecorations = SystemDecorations.BorderOnly;
                break;
            case AirAppWindowChromeMode.FullScreen:
                SystemDecorations = SystemDecorations.None;
                WindowState = WindowState.FullScreen;
                break;
            case AirAppWindowChromeMode.Tool:
                SystemDecorations = SystemDecorations.Full;
                ShowInTaskbar = false;
                break;
        }
    }
}
