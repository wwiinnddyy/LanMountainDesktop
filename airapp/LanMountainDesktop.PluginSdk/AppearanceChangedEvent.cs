namespace LanMountainDesktop.PluginSdk;

/// <summary>
/// Provides the latest read-only appearance snapshot when host appearance values change.
/// </summary>
public sealed class AppearanceChangedEvent : EventArgs
{
    public AppearanceChangedEvent(
        PluginAppearanceSnapshot snapshot,
        IReadOnlyCollection<AppearanceProperty> changedProperties)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changedProperties);

        Snapshot = snapshot;
        ChangedProperties = changedProperties;
    }

    public PluginAppearanceSnapshot Snapshot { get; }

    public IReadOnlyCollection<AppearanceProperty> ChangedProperties { get; }

    public bool CornerRadiusChanged => HasChanged(AppearanceProperty.CornerRadius);

    public bool ThemeVariantChanged => HasChanged(AppearanceProperty.ThemeVariant);

    public bool AccentColorChanged => HasChanged(AppearanceProperty.AccentColor);

    public bool CornerRadiusStyleChanged => HasChanged(AppearanceProperty.CornerRadiusStyle);

    public bool WallpaperChanged => HasChanged(AppearanceProperty.Wallpaper);

    public bool SystemMaterialModeChanged => HasChanged(AppearanceProperty.SystemMaterialMode);

    public bool ColorSourceChanged => HasChanged(AppearanceProperty.ColorSource);

    public bool ColorRolesChanged => HasChanged(AppearanceProperty.ColorRoles);

    public bool MaterialSurfacesChanged => HasChanged(AppearanceProperty.MaterialSurfaces);

    public bool WallpaperSeedCandidatesChanged => HasChanged(AppearanceProperty.WallpaperSeedCandidates);

    public bool HasChanged(AppearanceProperty property)
    {
        return ChangedProperties.Contains(AppearanceProperty.All) ||
            ChangedProperties.Contains(property);
    }

    public bool HasAnyChanges => ChangedProperties.Count > 0;
}

public enum AppearanceProperty
{
    CornerRadius,
    ThemeVariant,
    AccentColor,
    CornerRadiusStyle,
    Wallpaper,
    SystemMaterialMode,
    ColorSource,
    ColorRoles,
    MaterialSurfaces,
    WallpaperSeedCandidates,
    All
}
