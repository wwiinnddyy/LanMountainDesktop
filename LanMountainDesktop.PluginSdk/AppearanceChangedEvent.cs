namespace LanMountainDesktop.PluginSdk;

/// <summary>
/// 外观变更事件参数，当主题、圆角或其他外观属性变化时触发。
/// </summary>
public sealed class AppearanceChangedEvent : EventArgs
{
    /// <summary>
    /// 创建外观变更事件实例。
    /// </summary>
    /// <param name="snapshot">当前外观快照</param>
    /// <param name="changedProperties">变更的属性集合</param>
    public AppearanceChangedEvent(
        PluginAppearanceSnapshot snapshot,
        IReadOnlyCollection<AppearanceProperty> changedProperties)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changedProperties);

        Snapshot = snapshot;
        ChangedProperties = changedProperties;
    }

    /// <summary>
    /// 当前外观快照。
    /// </summary>
    public PluginAppearanceSnapshot Snapshot { get; }

    /// <summary>
    /// 变更的属性集合。
    /// </summary>
    public IReadOnlyCollection<AppearanceProperty> ChangedProperties { get; }

    /// <summary>
    /// 圆角是否发生变化。
    /// </summary>
    public bool CornerRadiusChanged => ChangedProperties.Contains(AppearanceProperty.CornerRadius);

    /// <summary>
    /// 主题变体（亮色/暗色）是否发生变化。
    /// </summary>
    public bool ThemeVariantChanged => ChangedProperties.Contains(AppearanceProperty.ThemeVariant);

    /// <summary>
    /// 强调色是否发生变化。
    /// </summary>
    public bool AccentColorChanged => ChangedProperties.Contains(AppearanceProperty.AccentColor);

    /// <summary>
    /// 圆角风格是否发生变化。
    /// </summary>
    public bool CornerRadiusStyleChanged => ChangedProperties.Contains(AppearanceProperty.CornerRadiusStyle);

    /// <summary>
    /// 检查指定属性是否发生变化。
    /// </summary>
    /// <param name="property">要检查的属性</param>
    /// <returns>如果属性发生变化则返回 true</returns>
    public bool HasChanged(AppearanceProperty property)
    {
        return ChangedProperties.Contains(property);
    }

    /// <summary>
    /// 检查是否有任何外观属性发生变化。
    /// </summary>
    public bool HasAnyChanges => ChangedProperties.Count > 0;
}

/// <summary>
/// 可变更的外观属性枚举。
/// </summary>
public enum AppearanceProperty
{
    /// <summary>
    /// 圆角Token值发生变化。
    /// </summary>
    CornerRadius,

    /// <summary>
    /// 主题变体（亮色/暗色）发生变化。
    /// </summary>
    ThemeVariant,

    /// <summary>
    /// 强调色发生变化。
    /// </summary>
    AccentColor,

    /// <summary>
    /// 圆角风格（Sharp/Balanced/Rounded/Open）发生变化。
    /// </summary>
    CornerRadiusStyle,

    /// <summary>
    /// 壁纸发生变化。
    /// </summary>
    Wallpaper,

    /// <summary>
    /// 系统材质模式发生变化。
    /// </summary>
    SystemMaterialMode,

    /// <summary>
    /// 所有外观属性（用于批量更新）。
    /// </summary>
    All
}
