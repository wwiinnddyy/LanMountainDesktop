using Avalonia.Controls;

namespace LanMountainDesktop.PluginSdk;

public sealed class PluginDesktopComponentEditorRegistration
{
    public PluginDesktopComponentEditorRegistration(
        string componentId,
        Func<IServiceProvider, PluginDesktopComponentEditorContext, Control> editorFactory,
        double preferredWidth = 720d,
        double preferredHeight = 540d,
        double minScale = 0.85d,
        double maxScale = 1.45d)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(editorFactory);

        if (preferredWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredWidth));
        }

        if (preferredHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredHeight));
        }

        if (minScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minScale));
        }

        if (maxScale < minScale)
        {
            throw new ArgumentOutOfRangeException(nameof(maxScale));
        }

        ComponentId = componentId.Trim();
        EditorFactory = editorFactory;
        PreferredWidth = preferredWidth;
        PreferredHeight = preferredHeight;
        MinScale = minScale;
        MaxScale = maxScale;
        AspectRatio = preferredWidth / preferredHeight;
    }

    public PluginDesktopComponentEditorRegistration(
        string componentId,
        Func<PluginDesktopComponentEditorContext, Control> editorFactory,
        double preferredWidth = 720d,
        double preferredHeight = 540d,
        double minScale = 0.85d,
        double maxScale = 1.45d)
        : this(
            componentId,
            (_, context) => editorFactory(context),
            preferredWidth,
            preferredHeight,
            minScale,
            maxScale)
    {
    }

    public string ComponentId { get; }

    public Func<IServiceProvider, PluginDesktopComponentEditorContext, Control> EditorFactory { get; }

    public double PreferredWidth { get; }

    public double PreferredHeight { get; }

    public double MinScale { get; }

    public double MaxScale { get; }

    public double AspectRatio { get; }
}
