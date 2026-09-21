using System;
using System.IO;

namespace LanMountainDesktop.Shared.Data;

/// <summary>
/// 每用户数据根目录 <c>%LocalAppData%\LanMountainDesktop</c> 的真源。
/// 宿主、首启向导（Launcher）、Core 里的路径解析器与安装器都往这里落东西：
/// 设置、日志、崩溃转储、启动状态、数据位置配置。目录名抄错一个字母不会报错，
/// 只会让某个二进制去一个空目录里找用户的数据——用户看到的就是"设置没了"。
/// </summary>
public static class UserDataRoot
{
    /// <summary>数据根目录名本身，给"还要再往下拼子目录"的调用点用。</summary>
    public const string FolderName = "LanMountainDesktop";

    /// <summary>settings.json 的文件名，宿主读、首启向导写。</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>完整的数据根；拿不到 LocalApplicationData 时返回空串，由调用点决定兜底。</summary>
    public static string Resolve() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    /// <summary><c>{数据根}/settings.json</c>。</summary>
    public static string ResolveSettingsPath() => Path.Combine(Resolve(), SettingsFileName);
}
