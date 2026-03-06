using System;

namespace LanMountainDesktop.Models;

public static class BaiduHotSearchSourceTypes
{
    public const string Official = "Official";
    public const string ThirdPartyRss = "ThirdPartyRss";

    public static string Normalize(string? sourceType)
    {
        if (string.Equals(sourceType, ThirdPartyRss, StringComparison.OrdinalIgnoreCase))
        {
            return ThirdPartyRss;
        }

        return Official;
    }
}
