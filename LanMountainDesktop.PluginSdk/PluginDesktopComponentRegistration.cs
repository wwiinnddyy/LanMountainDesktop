using Avalonia.Controls;

namespace LanMountainDesktop.PluginSdk;

public sealed class PluginDesktopComponentRegistration
{
    public PluginDesktopComponentRegistration(
        string componentId,
        string displayName,
        Func<PluginDesktopComponentContext, Control> controlFactory,
        string iconKey = "PuzzlePiece",
        string category = "Plugins",
        int minWidthCells = 2,
        int minHeightCells = 2,
        bool allowDesktopPlacement = true,
        bool allowStatusBarPlacement = false,
        PluginDesktopComponentResizeMode resizeMode = PluginDesktopComponentResizeMode.Proportional,
        string? displayNameLocalizationKey = null,
        Func<double, double>? cornerRadiusResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(controlFactory);

        ComponentId = componentId.Trim();
        DisplayName = displayName.Trim();
        DisplayNameLocalizationKey = string.IsNullOrWhiteSpace(displayNameLocalizationKey)
            ? null
            : displayNameLocalizationKey.Trim();
        ControlFactory = controlFactory;
        IconKey = iconKey.Trim();
        Category = category.Trim();
        MinWidthCells = Math.Max(1, minWidthCells);
        MinHeightCells = Math.Max(1, minHeightCells);
        AllowDesktopPlacement = allowDesktopPlacement;
        AllowStatusBarPlacement = allowStatusBarPlacement;
        ResizeMode = resizeMode;
        CornerRadiusResolver = cornerRadiusResolver;
    }

    public string ComponentId { get; }

    public string DisplayName { get; }

    public string? DisplayNameLocalizationKey { get; }

    public Func<PluginDesktopComponentContext, Control> ControlFactory { get; }

    public string IconKey { get; }

    public string Category { get; }

    public int MinWidthCells { get; }

    public int MinHeightCells { get; }

    public bool AllowDesktopPlacement { get; }

    public bool AllowStatusBarPlacement { get; }

    public PluginDesktopComponentResizeMode ResizeMode { get; }

    public Func<double, double>? CornerRadiusResolver { get; }
}
