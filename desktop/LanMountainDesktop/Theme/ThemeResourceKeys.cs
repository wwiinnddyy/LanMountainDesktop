namespace LanMountainDesktop.Theme;

/**
 * 自适应主题资源的键名只认这一处。注册方（GlassEffectService、ThemeColorSystemService）与读取方
 * （组件、编辑器、MainWindow）此前各写各的字符串，拼错一个字母不会编译报错，只会让那块 UI 静默失色。
 * .axaml 里的 StaticResource 引用仍是字面量——XAML 取不到 C# 常量，这条约束只管 C# 这一侧。
 */
public static class ThemeResourceKeys
{
    public const string AccentBrush = "AdaptiveAccentBrush";
    public const string ButtonBackgroundBrush = "AdaptiveButtonBackgroundBrush";
    public const string ButtonBorderBrush = "AdaptiveButtonBorderBrush";
    public const string ButtonHoverBackgroundBrush = "AdaptiveButtonHoverBackgroundBrush";
    public const string ButtonPressedBackgroundBrush = "AdaptiveButtonPressedBackgroundBrush";
    public const string CardBackgroundBrush = "AdaptiveCardBackgroundBrush";
    public const string DesktopComponentHostBackgroundBrush = "AdaptiveDesktopComponentHostBackgroundBrush";
    public const string DesktopComponentHostBorderBrush = "AdaptiveDesktopComponentHostBorderBrush";
    public const string DesktopComponentHostOpacity = "AdaptiveDesktopComponentHostOpacity";
    public const string DockBackgroundBrush = "AdaptiveDockBackgroundBrush";
    public const string DockBorderBrush = "AdaptiveDockBorderBrush";
    public const string DockGlassBackgroundBrush = "AdaptiveDockGlassBackgroundBrush";
    public const string DockGlassBorderBrush = "AdaptiveDockGlassBorderBrush";
    public const string DockOpacity = "AdaptiveDockOpacity";
    public const string GlassNoiseOpacity = "AdaptiveGlassNoiseOpacity";
    public const string GlassOverlayBackgroundBrush = "AdaptiveGlassOverlayBackgroundBrush";
    public const string GlassOverlayBlurRadius = "AdaptiveGlassOverlayBlurRadius";
    public const string GlassOverlayOpacity = "AdaptiveGlassOverlayOpacity";
    public const string GlassPanelBackgroundBrush = "AdaptiveGlassPanelBackgroundBrush";
    public const string GlassPanelBlurRadius = "AdaptiveGlassPanelBlurRadius";
    public const string GlassPanelBorderBrush = "AdaptiveGlassPanelBorderBrush";
    public const string GlassPanelOpacity = "AdaptiveGlassPanelOpacity";
    public const string GlassStrongBackgroundBrush = "AdaptiveGlassStrongBackgroundBrush";
    public const string GlassStrongBlurRadius = "AdaptiveGlassStrongBlurRadius";
    public const string GlassStrongBorderBrush = "AdaptiveGlassStrongBorderBrush";
    public const string GlassStrongOpacity = "AdaptiveGlassStrongOpacity";
    public const string NavItemBackgroundBrush = "AdaptiveNavItemBackgroundBrush";
    public const string NavItemHoverBackgroundBrush = "AdaptiveNavItemHoverBackgroundBrush";
    public const string NavItemSelectedBackgroundBrush = "AdaptiveNavItemSelectedBackgroundBrush";
    public const string NavSelectedTextBrush = "AdaptiveNavSelectedTextBrush";
    public const string NavSelectionIndicatorBrush = "AdaptiveNavSelectionIndicatorBrush";
    public const string NavTextBrush = "AdaptiveNavTextBrush";
    public const string OnAccentBrush = "AdaptiveOnAccentBrush";
    public const string PrimaryBrush = "AdaptivePrimaryBrush";
    public const string SecondaryBrush = "AdaptiveSecondaryBrush";
    public const string SettingsWindowBackgroundBrush = "AdaptiveSettingsWindowBackgroundBrush";
    public const string SettingsWindowBorderBrush = "AdaptiveSettingsWindowBorderBrush";
    public const string SettingsWindowTintBrush = "AdaptiveSettingsWindowTintBrush";
    public const string StatusBarBackgroundBrush = "AdaptiveStatusBarBackgroundBrush";
    public const string StatusBarBorderBrush = "AdaptiveStatusBarBorderBrush";
    public const string StatusBarComponentHostBackgroundBrush = "AdaptiveStatusBarComponentHostBackgroundBrush";
    public const string StatusBarComponentHostBorderBrush = "AdaptiveStatusBarComponentHostBorderBrush";
    public const string StatusBarComponentHostOpacity = "AdaptiveStatusBarComponentHostOpacity";
    public const string StatusBarOpacity = "AdaptiveStatusBarOpacity";
    public const string SurfaceBaseBrush = "AdaptiveSurfaceBaseBrush";
    public const string SurfaceOverlayBrush = "AdaptiveSurfaceOverlayBrush";
    public const string SurfaceRaisedBrush = "AdaptiveSurfaceRaisedBrush";
    public const string TextAccentBrush = "AdaptiveTextAccentBrush";
    public const string TextMutedBrush = "AdaptiveTextMutedBrush";
    public const string TextPrimaryBrush = "AdaptiveTextPrimaryBrush";
    public const string TextSecondaryBrush = "AdaptiveTextSecondaryBrush";
    public const string ToggleBorderBrush = "AdaptiveToggleBorderBrush";
    public const string ToggleOffBrush = "AdaptiveToggleOffBrush";
    public const string ToggleOnBrush = "AdaptiveToggleOnBrush";
    public const string WindowBackgroundBrush = "AdaptiveWindowBackgroundBrush";
    public const string WindowBorderBrush = "AdaptiveWindowBorderBrush";
}
