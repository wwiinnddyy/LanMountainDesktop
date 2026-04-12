using Avalonia;

namespace LanMountainDesktop.PluginSdk;

/// <summary>
/// 插件外观辅助方法，提供统一的圆角和主题资源访问。
/// </summary>
public static class PluginAppearanceHelper
{
    /// <summary>
    /// 获取桌面组件主外壳圆角半径。
    /// 这是组件最外层边框应该使用的圆角值，对应 DesignCornerRadiusComponent 资源。
    /// </summary>
    /// <param name="context">外观上下文</param>
    /// <returns>主外壳圆角半径（像素）</returns>
    public static double GetShellCornerRadius(this IPluginAppearanceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ResolveCornerRadius(PluginCornerRadiusPreset.Component);
    }

    /// <summary>
    /// 获取内部卡片圆角半径。
    /// 用于组件内部的次级卡片、内容区块等。
    /// </summary>
    /// <param name="context">外观上下文</param>
    /// <returns>内部卡片圆角半径（像素）</returns>
    public static double GetCardCornerRadius(this IPluginAppearanceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ResolveCornerRadius(PluginCornerRadiusPreset.Sm);
    }

    /// <summary>
    /// 获取控件圆角半径。
    /// 用于按钮、输入框、标签等交互控件。
    /// </summary>
    /// <param name="context">外观上下文</param>
    /// <returns>控件圆角半径（像素）</returns>
    public static double GetControlCornerRadius(this IPluginAppearanceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ResolveCornerRadius(PluginCornerRadiusPreset.Xs);
    }

    /// <summary>
    /// 获取徽章/标签圆角半径。
    /// 用于小徽章、标签、角标等微元素。
    /// </summary>
    /// <param name="context">外观上下文</param>
    /// <returns>徽章圆角半径（像素）</returns>
    public static double GetBadgeCornerRadius(this IPluginAppearanceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ResolveCornerRadius(PluginCornerRadiusPreset.Micro);
    }

    /// <summary>
    /// 获取中等面板圆角半径。
    /// 用于悬浮菜单、小提示框、子面板等。
    /// </summary>
    /// <param name="context">外观上下文</param>
    /// <returns>中等面板圆角半径（像素）</returns>
    public static double GetMediumPanelCornerRadius(this IPluginAppearanceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ResolveCornerRadius(PluginCornerRadiusPreset.Md);
    }

    /// <summary>
    /// 获取大面板圆角半径。
    /// 用于对话框、设置面板等大型容器（非桌面组件）。
    /// </summary>
    /// <param name="context">外观上下文</param>
    /// <returns>大面板圆角半径（像素）</returns>
    public static double GetLargePanelCornerRadius(this IPluginAppearanceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ResolveCornerRadius(PluginCornerRadiusPreset.Lg);
    }

    /// <summary>
    /// 将圆角预设转换为 Avalonia CornerRadius。
    /// </summary>
    /// <param name="context">外观上下文</param>
    /// <param name="preset">圆角预设</param>
    /// <returns>Avalonia CornerRadius 结构</returns>
    public static CornerRadius ToCornerRadius(this IPluginAppearanceContext context, PluginCornerRadiusPreset preset)
    {
        ArgumentNullException.ThrowIfNull(context);
        var radius = context.ResolveCornerRadius(preset);
        return new CornerRadius(radius);
    }

    /// <summary>
    /// 获取当前主题变体（亮色/暗色）。
    /// </summary>
    /// <param name="context">外观上下文</param>
    /// <returns>是否为暗色主题</returns>
    public static bool IsDarkTheme(this IPluginAppearanceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return string.Equals(context.Snapshot.ThemeVariant, "Dark", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取当前主题变体字符串。
    /// </summary>
    /// <param name="context">外观上下文</param>
    /// <returns>主题变体字符串（"Light" 或 "Dark"）</returns>
    public static string GetThemeVariant(this IPluginAppearanceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Snapshot.ThemeVariant;
    }
}

/// <summary>
/// 内部元素层级，用于区分不同层级的圆角需求。
/// </summary>
public enum InnerElementLevel
{
    /// <summary>
    /// 内部卡片：使用 Sm token（14px @ 1.0x）
    /// </summary>
    Card,

    /// <summary>
    /// 交互控件：使用 Xs token（12px @ 1.0x）
    /// </summary>
    Control,

    /// <summary>
    /// 微元素徽章：使用 Micro token（6px @ 1.0x）
    /// </summary>
    Badge
}
