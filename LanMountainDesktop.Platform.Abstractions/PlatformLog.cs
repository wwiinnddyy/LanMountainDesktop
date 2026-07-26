namespace LanMountainDesktop.Platform.Abstractions;

/// <summary>
/// 平台层日志桥。平台实现项目不引用宿主，
/// 宿主在启动时通过 <see cref="SetSink"/> 接入自己的日志系统（AppLogger）。
/// 未接入时日志静默丢弃。
/// </summary>
public static class PlatformLog
{
    private static IPlatformLogSink? _sink;

    public static void SetSink(IPlatformLogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    public static void Info(string category, string message) => _sink?.Info(category, message);

    public static void Warn(string category, string message, Exception? exception = null) =>
        _sink?.Warn(category, message, exception);

    public static void Error(string category, string message, Exception? exception = null) =>
        _sink?.Error(category, message, exception);
}

/// <summary>
/// 平台层日志输出目标。
/// </summary>
public interface IPlatformLogSink
{
    void Info(string category, string message);
    void Warn(string category, string message, Exception? exception = null);
    void Error(string category, string message, Exception? exception = null);
}
