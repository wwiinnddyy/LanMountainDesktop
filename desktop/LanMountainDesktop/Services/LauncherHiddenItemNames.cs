using System;
using System.IO;

namespace LanMountainDesktop.Services;

/// <summary>
/// 启动台"隐藏项"没有友好名字时的兜底显示名，唯一一份规则：
/// 斜杠统一、取末段文件名、去掉扩展名，取不出像样的名字就退回原键，整条为空才是 <c>"Unknown"</c>。
/// 此前设置页（<c>LauncherSettingsPageViewModel</c>）与桌面分页（<c>MainWindow.DesktopPaging</c>）
/// 各抄一份逐字相同的 9 行——改了设置页那一份，桌面叠加层上的隐藏项标签不会跟着变，
/// 同一个隐藏项在两处显示成两个名字。
/// </summary>
public static class LauncherHiddenItemNames
{
    public static string FallbackDisplayName(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Unknown";
        }

        var normalized = key!.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        return string.IsNullOrWhiteSpace(fileName)
            ? key
            : fileName;
    }
}
