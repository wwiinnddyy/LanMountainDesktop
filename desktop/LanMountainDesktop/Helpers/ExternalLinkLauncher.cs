using System;
using System.Diagnostics;

namespace LanMountainDesktop.Helpers;

/// <summary>
/// 组件打开外部链接的唯一入口：先做 http/https 归一化，再交给系统默认程序。
/// 归一化与兜底策略此前在 6 个服务/组件里逐字复制，任何一份漏掉 scheme 校验都是口子。
/// 打开文件、文件夹和本地程序的入口在各自组件里，语义不同，不走这里。
/// </summary>
public static class ExternalLinkLauncher
{
    public static string? NormalizeHttpUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var candidate = rawUrl.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.ToString();
    }

    public static bool TryOpen(string? rawUrl)
    {
        var normalizedUrl = NormalizeHttpUrl(rawUrl);
        if (normalizedUrl is null)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = normalizedUrl,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
