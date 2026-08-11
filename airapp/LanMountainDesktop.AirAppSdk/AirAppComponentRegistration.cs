using Avalonia.Controls;

namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppComponentRegistration
{
    public AirAppComponentRegistration(
        Func<IServiceProvider, AirAppComponentContext, Control> controlFactory,
        AirAppComponentOptions options)
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
        Description = string.IsNullOrWhiteSpace(options.Description)
            ? null
            : options.Description.Trim();
        DescriptionLocalizationKey = string.IsNullOrWhiteSpace(options.DescriptionLocalizationKey)
            ? null
            : options.DescriptionLocalizationKey.Trim();
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

    public AirAppComponentRegistration(
        Func<AirAppComponentContext, Control> controlFactory,
        AirAppComponentOptions options)
        : this((_, context) => controlFactory(context), options)
    {
    }

    public string ComponentId { get; }

    public string DisplayName { get; }

    public string? DisplayNameLocalizationKey { get; }

    public string? Description { get; }

    public string? DescriptionLocalizationKey { get; }

    public Func<IServiceProvider, AirAppComponentContext, Control> ControlFactory { get; }

    public string IconKey { get; }

    public string Category { get; }

    public int MinWidthCells { get; }

    public int MinHeightCells { get; }

    public bool AllowDesktopPlacement { get; }

    public bool AllowStatusBarPlacement { get; }

    public AirAppComponentResizeMode ResizeMode { get; }

    public AirAppCornerRadiusPreset CornerRadiusPreset { get; }

    public Func<IAirAppAppearanceContext, double, double>? CornerRadiusResolver { get; }

    public double ResolveCornerRadius(IAirAppAppearanceContext appearance, double cellSize)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        var resolved = CornerRadiusResolver is not null
            ? CornerRadiusResolver(appearance, Math.Max(1d, cellSize))
            : CornerRadiusPreset == AirAppCornerRadiusPreset.Default
                ? appearance.ResolveCornerRadius(AirAppCornerRadiusPreset.Component)
                : appearance.ResolveCornerRadius(CornerRadiusPreset);

        return double.IsFinite(resolved)
            ? Math.Max(0d, resolved)
            : appearance.ResolveCornerRadius(AirAppCornerRadiusPreset.Component);
    }
}
