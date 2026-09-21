namespace LanMountainDesktop.Shared.Contracts.Data;

/// <summary>
/// 数据位置配置（<c>data-location.config.json</c>）的磁盘契约：写它的是启动器，读它的是宿主与
/// 启动器两侧，中间没有共享类型，全靠这几个名字逐字对齐。文件名或字段名抄错的症状不是报错，
/// 而是"数据位置设置静默失效"——便携模式的用户会被当成系统模式，数据看起来"丢了"。
/// </summary>
/// <remarks>
/// 字段名是启动器那侧 JSON 源生成器按 camelCase 落盘的结果，也是宿主那侧手工读字典用的键。
/// 两边都从这里取值，改属性名时不会再出现"只改了一边"。
/// </remarks>
public static class DataLocationContract
{
    /// <summary>配置文件名，位于 <c>{安装根}/.Launcher</c> 下。</summary>
    public const string ConfigFileName = "data-location.config.json";

    /// <summary>便携模式下默认的数据文件夹名（安装根目录下的 Desktop）。</summary>
    public const string DesktopFolderName = "Desktop";

    public const string ModePropertyName = "dataLocationMode";

    public const string SystemPathPropertyName = "systemDataPath";

    public const string PortablePathPropertyName = "portableDataPath";

    /// <summary>两值口径：系统数据目录（%LocalAppData%\\LanMountainDesktop）。</summary>
    public const string SystemModeValue = "System";

    /// <summary>两值口径：跟着安装目录走。</summary>
    public const string PortableModeValue = "Portable";
}
