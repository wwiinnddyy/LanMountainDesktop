using System.Diagnostics;

namespace LanMountainDesktop.AirAppRuntime;

internal static class AirAppRuntimeLogger
{
    public static void Info(string message) => Trace.WriteLine($"[AirAppRuntime] INFO {message}");

    public static void Warn(string message) => Trace.WriteLine($"[AirAppRuntime] WARN {message}");

    public static void Warn(string message, Exception ex) =>
        Trace.WriteLine($"[AirAppRuntime] WARN {message} {ex}");

    public static void Error(string message, Exception ex) =>
        Trace.WriteLine($"[AirAppRuntime] ERROR {message} {ex}");
}
