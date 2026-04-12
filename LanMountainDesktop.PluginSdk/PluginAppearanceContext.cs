namespace LanMountainDesktop.PluginSdk;

/// <summary>
/// 插件外观上下文实现，提供主题、圆角等外观资源的访问和变更通知。
/// </summary>
public sealed class PluginAppearanceContext : IPluginAppearanceContext
{
    private PluginAppearanceSnapshot _snapshot;

    /// <summary>
    /// 创建插件外观上下文实例。
    /// </summary>
    /// <param name="snapshot">初始外观快照</param>
    public PluginAppearanceContext(PluginAppearanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.CornerRadiusTokens);

        _snapshot = snapshot with
        {
            ThemeVariant = string.IsNullOrWhiteSpace(snapshot.ThemeVariant)
                ? "Unknown"
                : snapshot.ThemeVariant.Trim()
        };
    }

    /// <inheritdoc />
    public PluginAppearanceSnapshot Snapshot => _snapshot;

    /// <inheritdoc />
    public event EventHandler<AppearanceChangedEvent>? Changed;

    /// <summary>
    /// 更新外观快照并触发变更事件。
    /// 此方法由宿主调用，用于在主题、圆角等外观属性变化时通知插件。
    /// </summary>
    /// <param name="newSnapshot">新的外观快照</param>
    /// <param name="changedProperties">变更的属性集合</param>
    public void UpdateSnapshot(PluginAppearanceSnapshot newSnapshot, IReadOnlyCollection<AppearanceProperty> changedProperties)
    {
        ArgumentNullException.ThrowIfNull(newSnapshot);
        ArgumentNullException.ThrowIfNull(changedProperties);

        _snapshot = newSnapshot with
        {
            ThemeVariant = string.IsNullOrWhiteSpace(newSnapshot.ThemeVariant)
                ? "Unknown"
                : newSnapshot.ThemeVariant.Trim()
        };

        if (changedProperties.Count > 0)
        {
            Changed?.Invoke(this, new AppearanceChangedEvent(_snapshot, changedProperties));
        }
    }

    /// <inheritdoc />
    public double ResolveScaledCornerRadius(double baseRadius, double? minimum = null, double? maximum = null)
    {
        var value = Math.Max(0d, baseRadius);
        if (!minimum.HasValue && !maximum.HasValue)
        {
            return value;
        }

        var clampedMin = minimum ?? value;
        var clampedMax = maximum ?? value;
        return Math.Clamp(value, clampedMin, clampedMax);
    }

    /// <inheritdoc />
    public double ResolveCornerRadius(PluginCornerRadiusPreset preset, double? minimum = null, double? maximum = null)
    {
        var resolved = Math.Max(0d, _snapshot.CornerRadiusTokens.Get(preset));
        if (!minimum.HasValue && !maximum.HasValue)
        {
            return resolved;
        }

        var clampedMin = minimum ?? 0d;
        var clampedMax = maximum ?? double.MaxValue;
        if (clampedMin > clampedMax)
        {
            (clampedMin, clampedMax) = (clampedMax, clampedMin);
        }

        return Math.Clamp(resolved, clampedMin, clampedMax);
    }
}
