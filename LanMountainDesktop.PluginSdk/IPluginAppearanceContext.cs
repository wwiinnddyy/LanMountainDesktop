namespace LanMountainDesktop.PluginSdk;

/// <summary>
/// 插件外观上下文接口，提供主题、圆角等外观资源的访问和变更通知。
/// </summary>
public interface IPluginAppearanceContext
{
    /// <summary>
    /// 当前外观快照。
    /// </summary>
    PluginAppearanceSnapshot Snapshot { get; }

    /// <summary>
    /// 外观变更事件。当主题、圆角或其他外观属性发生变化时触发。
    /// </summary>
    event EventHandler<AppearanceChangedEvent>? Changed;

    /// <summary>
    /// 解析带缩放的圆角半径。
    /// </summary>
    /// <param name="baseRadius">基础圆角半径</param>
    /// <param name="minimum">最小值（可选）</param>
    /// <param name="maximum">最大值（可选）</param>
    /// <returns>解析后的圆角半径</returns>
    double ResolveScaledCornerRadius(double baseRadius, double? minimum = null, double? maximum = null);

    /// <summary>
    /// 根据预设解析圆角半径。
    /// </summary>
    /// <param name="preset">圆角预设</param>
    /// <param name="minimum">最小值（可选）</param>
    /// <param name="maximum">最大值（可选）</param>
    /// <returns>解析后的圆角半径</returns>
    double ResolveCornerRadius(PluginCornerRadiusPreset preset, double? minimum = null, double? maximum = null);
}
