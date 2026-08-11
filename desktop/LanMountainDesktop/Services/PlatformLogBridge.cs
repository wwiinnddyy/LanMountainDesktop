using System;
using LanMountainDesktop.Platform.Abstractions;

namespace LanMountainDesktop.Services;

/// <summary>
/// 将平台层日志（PlatformLog）接入宿主 AppLogger。
/// 在应用启动早期调用 <see cref="Install"/> 一次。
/// </summary>
internal static class PlatformLogBridge
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        PlatformLog.SetSink(new AppLoggerSink());
    }

    private sealed class AppLoggerSink : IPlatformLogSink
    {
        public void Info(string category, string message) => AppLogger.Info(category, message);

        public void Warn(string category, string message, Exception? exception = null) =>
            AppLogger.Warn(category, message, exception);

        public void Error(string category, string message, Exception? exception = null) =>
            AppLogger.Error(category, message, exception);
    }
}
