using Avalonia.Controls;

namespace LanMountainDesktop.PluginSdk;

public sealed class PluginDesktopComponentRegistration
{
    public PluginDesktopComponentRegistration(
        Func<IServiceProvider, PluginDesktopComponentContext, Control> controlFactory,
        PluginDesktopComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(controlFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ComponentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.IconKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Category);

        ComponentId = options.ComponentId.Trim();
        DisplayName = options.DisplayName.Trim();
        DisplayNameLocalizationKey = string.IsNullOrWhiteSpace(options.DisplayNameLocalizationKey)
            ? null
            : options.DisplayNameLocalizationKey.Trim();
        ControlFactory = controlFactory;
        IconKey = options.IconKey.Trim();
        Category = options.Category.Trim();
        MinWidthCells = Math.Max(1, options.MinWidthCells);
        MinHeightCells = Math.Max(1, options.MinHeightCells);
        AllowDesktopPlacement = options.AllowDesktopPlacement;
        AllowStatusBarPlacement = options.AllowStatusBarPlacement;
        ResizeMode = options.ResizeMode;
        CornerRadiusPreset = options.CornerRadiusPreset;
        CornerRadiusResolver = options.CornerRadiusResolver;
    }

    public PluginDesktopComponentRegistration(
        Func<PluginDesktopComponentContext, Control> controlFactory,
        PluginDesktopComponentOptions options)
        : this((_, context) => controlFactory(context), options)
    {
    }

    public string ComponentId { get; }

    public string DisplayName { get; }

    public string? DisplayNameLocalizationKey { get; }

    public Func<IServiceProvider, PluginDesktopComponentContext, Control> ControlFactory { get; }

    public string IconKey { get; }

    public string Category { get; }

    public int MinWidthCells { get; }

    public int MinHeightCells { get; }

    public bool AllowDesktopPlacement { get; }

    public bool AllowStatusBarPlacement { get; }

    public PluginDesktopComponentResizeMode ResizeMode { get; }

    public PluginCornerRadiusPreset CornerRadiusPreset { get; }

    public Func<IPluginAppearanceContext, double, double>? CornerRadiusResolver { get; }

    public double ResolveCornerRadius(IPluginAppearanceContext appearance, double cellSize)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        var resolved = CornerRadiusResolver is not null
            ? CornerRadiusResolver(appearance, Math.Max(1d, cellSize))
            : CornerRadiusPreset == PluginCornerRadiusPreset.Default
                ? appearance.ResolveScaledCornerRadius(
                    Math.Clamp(Math.Max(1d, cellSize) * 0.22, 8, 18),
                    8,
                    18)
                : appearance.ResolveCornerRadius(CornerRadiusPreset);

        return double.IsFinite(resolved)
            ? Math.Max(0d, resolved)
            : appearance.ResolveCornerRadius(PluginCornerRadiusPreset.Default);
    }
}
