using Avalonia.Controls;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Base class for AirApp desktop component widgets.
/// Inherit from this to create custom desktop components.
/// </summary>
public abstract class AirAppWidgetBase : UserControl, IAirAppWidget
{
    private IAirAppComponentContext? _context;

    /// <summary>
    /// Gets or sets the component context.
    /// </summary>
    public IAirAppComponentContext Context
    {
        get => _context ?? throw new InvalidOperationException("Context has not been set yet.");
        set
        {
            _context = value;
            OnContextSet();
        }
    }

    /// <summary>
    /// Called when the context is first set.
    /// Override this to initialize based on context.
    /// </summary>
    protected virtual void OnContextSet()
    {
    }

    /// <summary>
    /// Called when the widget is attached to the desktop.
    /// </summary>
    public void OnAttached()
    {
        OnAttachedCore();
    }

    /// <summary>
    /// Called when the widget is detached from the desktop.
    /// </summary>
    public void OnDetached()
    {
        OnDetachedCore();
    }

    /// <summary>
    /// Called when the appearance has changed.
    /// </summary>
    /// <param name="snapshot">New appearance snapshot</param>
    public void OnAppearanceChanged(AirAppAppearanceSnapshot snapshot)
    {
        OnAppearanceChangedCore(snapshot);
    }

    /// <summary>
    /// Override this to handle widget attachment.
    /// </summary>
    protected virtual void OnAttachedCore()
    {
    }

    /// <summary>
    /// Override this to handle widget detachment.
    /// </summary>
    protected virtual void OnDetachedCore()
    {
    }

    /// <summary>
    /// Override this to handle appearance changes.
    /// </summary>
    /// <param name="snapshot">New appearance snapshot</param>
    protected virtual void OnAppearanceChangedCore(AirAppAppearanceSnapshot snapshot)
    {
    }
}
