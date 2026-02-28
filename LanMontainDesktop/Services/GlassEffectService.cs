using Avalonia.Controls;
using Avalonia.Media;
using LanMontainDesktop.Theme;

namespace LanMontainDesktop.Services;

public static class GlassEffectService
{
    private const double DayPanelBlurRadius = 40;
    private const double DayStrongBlurRadius = 60;
    private const double DayOverlayBlurRadius = 80;
    private const double NightPanelBlurRadius = 45;
    private const double NightStrongBlurRadius = 65;
    private const double NightOverlayBlurRadius = 85;

    public static void ApplyGlassResources(IResourceDictionary resources, ThemeColorContext context)
    {
        // Mica 材质：不透明，但混合壁纸颜色
        // 提取壁纸颜色的透明度（0-1），用于控制 Mica 效果强度
        var wallpaperTintOpacity = 0.15; // 壁纸颜色混合比例
        
        var neutralBase = context.IsNightMode ? Color.Parse("#FF202020") : Color.Parse("#FFF3F3F3");
        var neutralElevated = context.IsNightMode ? Color.Parse("#FF2C2C2C") : Color.Parse("#FFFAFAFA");
        
        // Mica 效果：将壁纸颜色混合到中性基色中
        var micaBackground = ColorMath.Blend(neutralBase, context.AccentColor, wallpaperTintOpacity);
        var micaElevated = ColorMath.Blend(neutralElevated, context.AccentColor, wallpaperTintOpacity * 0.8);
        
        // 按钮颜色
        var buttonBackground = context.IsNightMode ? 
            Color.FromArgb(0x33, micaBackground.R, micaBackground.G, micaBackground.B) :
            Color.FromArgb(0x4D, micaBackground.R, micaBackground.G, micaBackground.B);
            
        resources["AdaptiveButtonBackgroundBrush"] = new SolidColorBrush(buttonBackground);
        resources["AdaptiveButtonBorderBrush"] = new SolidColorBrush(
            Color.FromArgb(0x1A, neutralElevated.R, neutralElevated.G, neutralElevated.B));
        resources["AdaptiveButtonHoverBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(buttonBackground, context.IsNightMode ? (byte)0x4D : (byte)0x66));
        resources["AdaptiveButtonPressedBackgroundBrush"] = new SolidColorBrush(
            ColorMath.WithAlpha(buttonBackground, context.IsNightMode ? (byte)0x66 : (byte)0x80));

        // 面板颜色 - 使用 Mica 材质
        resources["AdaptiveGlassPanelBackgroundBrush"] = new SolidColorBrush(
            Color.FromArgb(context.IsNightMode ? (byte)0xF0 : (byte)0xF8, 
                          micaBackground.R, micaBackground.G, micaBackground.B));
        resources["AdaptiveGlassPanelBorderBrush"] = new SolidColorBrush(
            Color.FromArgb(0x1F, neutralElevated.R, neutralElevated.G, neutralElevated.B));
        resources["AdaptiveGlassStrongBackgroundBrush"] = new SolidColorBrush(
            Color.FromArgb(context.IsNightMode ? (byte)0xF4 : (byte)0xFB, 
                          micaElevated.R, micaElevated.G, micaElevated.B));
        resources["AdaptiveGlassStrongBorderBrush"] = new SolidColorBrush(
            Color.FromArgb(0x29, neutralElevated.R, neutralElevated.G, neutralElevated.B));
        resources["AdaptiveGlassOverlayBackgroundBrush"] = new SolidColorBrush(
            Color.FromArgb(context.IsNightMode ? (byte)0xE6 : (byte)0xF2, 
                          micaBackground.R, micaBackground.G, micaBackground.B));

        // 模糊半径（Mica 不需要强模糊）
        resources["AdaptiveGlassPanelBlurRadius"] = context.IsNightMode ? 20.0 : 30.0;
        resources["AdaptiveGlassStrongBlurRadius"] = context.IsNightMode ? 25.0 : 35.0;
        resources["AdaptiveGlassOverlayBlurRadius"] = context.IsNightMode ? 30.0 : 40.0;
        
        // 不透明度（Mica 材质接近不透明）
        resources["AdaptiveGlassPanelOpacity"] = context.IsNightMode ? 0.99 : 1.0;
        resources["AdaptiveGlassStrongOpacity"] = context.IsNightMode ? 1.0 : 1.0;
        resources["AdaptiveGlassOverlayOpacity"] = context.IsNightMode ? 0.94 : 0.97;
        resources["AdaptiveGlassNoiseOpacity"] = context.IsNightMode ? 0.01 : 0.008;
    }
}
