namespace LanMountainDesktop.Theme;

/**
 * 自适应主题资源的键名只认这一处。注册方（GlassEffectService、ThemeColorSystemService）与读取方
 * （组件、编辑器、MainWindow）此前各写各的字符串，拼错一个字母不会编译报错，只会让那块 UI 静默失色。
 * .axaml 里的 StaticResource 引用仍是字面量——XAML 取不到 C# 常量，这条约束只管 C# 这一侧。
 *
 * 2026-10-25 清理：删掉 28 个「只写不读」的键。判定方法是全仓 *.axaml + *.cs 双侧检索，
 * 除本文件外零命中。它们此前每次主题刷新都会被计算并写进资源字典，但没有任何一个消费者——
 * 属于纯开销。删掉后键数从 55 降到 27。
 */
public static class ThemeResourceKeys
{
    public const string AccentBrush = "AdaptiveAccentBrush";
    public const string ButtonBackgroundBrush = "AdaptiveButtonBackgroundBrush";
    public const string ButtonBorderBrush = "AdaptiveButtonBorderBrush";
    public const string ButtonHoverBackgroundBrush = "AdaptiveButtonHoverBackgroundBrush";
    public const string ButtonPressedBackgroundBrush = "AdaptiveButtonPressedBackgroundBrush";
    public const string DockGlassBackgroundBrush = "AdaptiveDockGlassBackgroundBrush";
    public const string DockGlassBorderBrush = "AdaptiveDockGlassBorderBrush";
    public const string GlassOverlayBackgroundBrush = "AdaptiveGlassOverlayBackgroundBrush";
    public const string GlassOverlayOpacity = "AdaptiveGlassOverlayOpacity";
    public const string GlassPanelBackgroundBrush = "AdaptiveGlassPanelBackgroundBrush";
    public const string GlassPanelBorderBrush = "AdaptiveGlassPanelBorderBrush";
    public const string GlassPanelOpacity = "AdaptiveGlassPanelOpacity";
    public const string GlassStrongBackgroundBrush = "AdaptiveGlassStrongBackgroundBrush";
    public const string GlassStrongBorderBrush = "AdaptiveGlassStrongBorderBrush";
    public const string GlassStrongOpacity = "AdaptiveGlassStrongOpacity";
    public const string NavItemBackgroundBrush = "AdaptiveNavItemBackgroundBrush";
    public const string NavItemSelectedBackgroundBrush = "AdaptiveNavItemSelectedBackgroundBrush";
    public const string OnAccentBrush = "AdaptiveOnAccentBrush";
    public const string SettingsWindowBackgroundBrush = "AdaptiveSettingsWindowBackgroundBrush";
    public const string SettingsWindowTintBrush = "AdaptiveSettingsWindowTintBrush";
    public const string SurfaceBaseBrush = "AdaptiveSurfaceBaseBrush";
    public const string SurfaceOverlayBrush = "AdaptiveSurfaceOverlayBrush";
    public const string SurfaceRaisedBrush = "AdaptiveSurfaceRaisedBrush";
    public const string TextAccentBrush = "AdaptiveTextAccentBrush";
    public const string TextMutedBrush = "AdaptiveTextMutedBrush";
    public const string TextPrimaryBrush = "AdaptiveTextPrimaryBrush";
    public const string TextSecondaryBrush = "AdaptiveTextSecondaryBrush";
}
