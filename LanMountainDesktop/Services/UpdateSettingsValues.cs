using System;

namespace LanMountainDesktop.Services;

public static class UpdateSettingsValues
{
    public const string ChannelStable = "stable";
    public const string ChannelPreview = "preview";

    public const string ModeManual = "manual";
    public const string ModeDownloadThenConfirm = "download_then_confirm";
    public const string ModeSilentOnExit = "silent_on_exit";

    public const string DownloadSourcePdc = "pdc";
    public const string DownloadSourceGitHub = "github";
    public const string DownloadSourceGhProxy = "gh-proxy";

    public const int DefaultDownloadThreads = 4;
    public const int MinDownloadThreads = 1;
    public const int MaxDownloadThreads = 128;
    public const string DefaultGhProxyBaseUrl = "https://gh-proxy.com/";

    public static string NormalizeChannel(string? value, bool includePrereleaseFallback = false)
    {
        if (string.Equals(value, ChannelPreview, StringComparison.OrdinalIgnoreCase))
        {
            return ChannelPreview;
        }

        if (string.Equals(value, ChannelStable, StringComparison.OrdinalIgnoreCase))
        {
            return ChannelStable;
        }

        return includePrereleaseFallback ? ChannelPreview : ChannelStable;
    }

    public static string NormalizeMode(string? value)
    {
        if (string.Equals(value, ModeManual, StringComparison.OrdinalIgnoreCase))
        {
            return ModeManual;
        }

        if (string.Equals(value, ModeSilentOnExit, StringComparison.OrdinalIgnoreCase))
        {
            return ModeSilentOnExit;
        }

        return ModeDownloadThenConfirm;
    }

    public static string NormalizeDownloadSource(string? value)
    {
        if (string.Equals(value, DownloadSourcePdc, StringComparison.OrdinalIgnoreCase))
        {
            return DownloadSourcePdc;
        }

        if (string.Equals(value, DownloadSourceGhProxy, StringComparison.OrdinalIgnoreCase))
        {
            return DownloadSourceGhProxy;
        }

        if (string.Equals(value, DownloadSourceGitHub, StringComparison.OrdinalIgnoreCase))
        {
            return DownloadSourceGitHub;
        }

        // Default to PDC. Runtime will fallback to GitHub if PDC is unavailable.
        return DownloadSourcePdc;
    }

    public static int NormalizeDownloadThreads(int value)
    {
        return Math.Clamp(value, MinDownloadThreads, MaxDownloadThreads);
    }
}
