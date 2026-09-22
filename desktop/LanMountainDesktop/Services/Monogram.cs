using System;
using System.Linq;

namespace LanMountainDesktop.Services;

/// <summary>
/// 从显示名生成头像占位字（首字母，最多两个，大写；读不出来就是 "?"）。
/// 此前这段 15 行在 <see cref="CurrentUserProfileService"/>（用户头像兜底）与
/// <c>MainWindow.DesktopPaging</c>（启动台磁贴）各抄一份，逐字相同：改了头像规则而磁贴没跟上，
/// 症状是同一个人在桌面上有两种缩写，不报错但看着就是两个人。
/// </summary>
public static class Monogram
{
    public static string From(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        var letters = text
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part[0])
            .Take(2)
            .ToArray();

        if (letters.Length == 0)
        {
            return "?";
        }

        return new string(letters).ToUpperInvariant();
    }
}
