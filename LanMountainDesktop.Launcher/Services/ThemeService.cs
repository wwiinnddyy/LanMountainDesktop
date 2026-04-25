using Avalonia;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 主题服务，管理启动器的主题设置
/// </summary>
public static class ThemeService
{
    private static ThemeVariant _currentTheme = ThemeVariant.Light;
    private static string _accentColor = "#0078D4";

    /// <summary>
    /// 获取当前主题
    /// </summary>
    public static ThemeVariant CurrentTheme => _currentTheme;

    /// <summary>
    /// 获取当前主题色
    /// </summary>
    public static string AccentColor => _accentColor;

    /// <summary>
    /// 应用主题设置
    /// </summary>
    public static void ApplyTheme(ThemeMode mode, string accentColor)
    {
        _currentTheme = mode switch
        {
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Light
        };
        _accentColor = accentColor;

        // 应用到当前应用程序
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = _currentTheme;
        }
    }

    /// <summary>
    /// 应用浅色主题
    /// </summary>
    public static void ApplyLightTheme(string accentColor)
    {
        ApplyTheme(ThemeMode.Light, accentColor);
    }

    /// <summary>
    /// 应用深色主题
    /// </summary>
    public static void ApplyDarkTheme(string accentColor)
    {
        ApplyTheme(ThemeMode.Dark, accentColor);
    }
}

/// <summary>
/// 主题模式
/// </summary>
public enum ThemeMode
{
    Light,
    Dark
}
