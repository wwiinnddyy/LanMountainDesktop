using System;

namespace LanMountainDesktop.Shared.Text;

/// <summary>
/// 字节数 → 显示文本的唯一一份口径（下载量/速度两块安装屏共用）。
///
/// 此前 MewInstaller 的 <c>InstallerState</c> 与 PLONDS 安装器的 <c>MainWindowViewModel</c>
/// 各抄了一份逐字相同的 <c>FormatBytes</c> + <c>ToSize</c>（含同一张 ["B","KB","MB","GB"] 单位表）。
/// 码表抄两处，补一处漏一处＝同一个包在两块安装屏上两个尺寸。
///
/// 口径钉死：1024 进位、<c>0.##</c> 两位小数、GB 封顶不再进位；
/// "已下载 / 总量" 只在总量为正时显示，总量缺失或零下载回空串。
/// </summary>
public static class ByteSizeText
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    /// <summary>把字节数格式化成 "0.## 单位"；1024 进位，GB 封顶。</summary>
    public static string ToSize(long value)
    {
        var size = (double)value;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {Units[unit]}";
    }

    /// <summary>"已下载 / 总量"；两者都无有效值时回空串，只有已下载时只显示已下载。</summary>
    public static string FormatBytes(long downloaded, long? total)
    {
        if (downloaded <= 0 && total is not > 0)
        {
            return string.Empty;
        }

        var downloadedText = ToSize(downloaded);
        return total is > 0 ? $"{downloadedText} / {ToSize(total.Value)}" : downloadedText;
    }
}
