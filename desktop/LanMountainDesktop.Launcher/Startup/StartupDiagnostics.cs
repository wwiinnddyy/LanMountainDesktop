using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Startup;

internal static class StartupDiagnostics
{
    private static readonly bool Enabled =
        string.Equals(
            Environment.GetEnvironmentVariable("LMD_LAUNCHER_STARTUP_DIAG"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    public static bool IsEnabled => Enabled;

    public static void Trace(string eventName, IReadOnlyDictionary<string, string?> fields)
    {
        if (!Enabled)
        {
            return;
        }

        var payload = new Dictionary<string, string?>(fields, StringComparer.OrdinalIgnoreCase)
        {
            ["event"] = eventName,
            ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        Logger.Info($"[startup-diag] {eventName}: {string.Join("; ", payload.Select(static kv => $"{kv.Key}={kv.Value}"))}");

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop",
                ".launcher",
                "diag");
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, $"startup-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            var line = JsonSerializer.Serialize(payload);
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to write startup diagnostic bundle: {ex.Message}");
        }
    }

    public static void TraceShellStatus(string source, PublicShellStatus? status, StartupStage? stage = null)
    {
        if (!Enabled)
        {
            return;
        }

        Trace(
            "shell_status",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = source,
                ["stage"] = stage?.ToString(),
                ["processId"] = status?.ProcessId.ToString(),
                ["publicIpcReady"] = status?.PublicIpcReady.ToString(),
                ["desktopVisible"] = status?.DesktopVisible.ToString(),
                ["mainWindowVisible"] = status?.MainWindowVisible.ToString(),
                ["mainWindowOpened"] = status?.MainWindowOpened.ToString(),
                ["shellState"] = status?.ShellState
            });
    }
}
