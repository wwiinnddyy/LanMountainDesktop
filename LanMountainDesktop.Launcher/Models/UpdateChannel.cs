namespace LanMountainDesktop.Launcher.Models;

/// <summary>
/// 更新频道
/// </summary>
public enum UpdateChannel
{
    /// <summary>
    /// 正式版 - 只检查 prerelease=false 的版本
    /// </summary>
    Stable,

    /// <summary>
    /// 预览版 - 检查所有版本(包括 prerelease=true)
    /// </summary>
    Preview
}
