using System.Collections.Generic;

namespace LanMountainDesktop.Services;

internal static class TelemetryEventNames
{
    internal static string DisplayName(string eventName) =>
        EventDisplayNames.TryGetValue(eventName, out var displayName)
            ? displayName
            : eventName;

    internal const string AppFirstLaunch = "app_first_launch";
    internal const string AppSessionStart = "app_session_start";
    internal const string AppSessionEnd = "app_session_end";
    internal const string MainWindowOpened = "main_window_opened";
    internal const string MainWindowClosed = "main_window_closed";
    internal const string SettingsWindowOpened = "settings_window_opened";
    internal const string SettingsWindowClosed = "settings_window_closed";
    internal const string SettingsNavigation = "settings_navigation";
    internal const string SettingsDrawerOpened = "settings_drawer_opened";
    internal const string SettingsDrawerClosed = "settings_drawer_closed";
    internal const string DesktopComponentPlaced = "desktop_component_placed";
    internal const string DesktopComponentMoved = "desktop_component_moved";
    internal const string DesktopComponentResized = "desktop_component_resized";
    internal const string DesktopComponentDeleted = "desktop_component_deleted";
    internal const string DesktopComponentEditorOpened = "desktop_component_editor_opened";
    internal const string ThemeChanged = "theme_changed";
    internal const string PluginInstalled = "plugin_installed";
    internal const string PluginUninstalled = "plugin_uninstalled";
    internal const string PluginEnabled = "plugin_enabled";
    internal const string PluginDisabled = "plugin_disabled";
    internal const string UpdateChecked = "update_checked";
    internal const string UpdateInstalled = "update_installed";
    internal const string AppCrash = "app_crash";

    internal const string SentryUnhandledException = "unhandled_exception";
    internal const string SentryTaskException = "task_exception";
    internal const string SentryShutdown = "shutdown";

    private static readonly Dictionary<string, string> EventDisplayNames = new()
    {
        [AppFirstLaunch] = "应用首次启动",
        [AppSessionStart] = "会话开始",
        [AppSessionEnd] = "会话结束",
        [MainWindowOpened] = "主窗口打开",
        [MainWindowClosed] = "主窗口关闭",
        [SettingsWindowOpened] = "设置窗口打开",
        [SettingsWindowClosed] = "设置窗口关闭",
        [SettingsNavigation] = "设置页导航",
        [SettingsDrawerOpened] = "设置抽屉打开",
        [SettingsDrawerClosed] = "设置抽屉关闭",
        [DesktopComponentPlaced] = "桌面组件放置",
        [DesktopComponentMoved] = "桌面组件移动",
        [DesktopComponentResized] = "桌面组件缩放",
        [DesktopComponentDeleted] = "桌面组件删除",
        [DesktopComponentEditorOpened] = "组件编辑器打开",
        [ThemeChanged] = "主题变更",
        [PluginInstalled] = "插件安装",
        [PluginUninstalled] = "插件卸载",
        [PluginEnabled] = "插件启用",
        [PluginDisabled] = "插件禁用",
        [UpdateChecked] = "更新检查",
        [UpdateInstalled] = "更新安装",
        [AppCrash] = "应用崩溃",
        [SentryUnhandledException] = "未处理异常",
        [SentryTaskException] = "任务异常",
        [SentryShutdown] = "应用关闭"
    };
}
