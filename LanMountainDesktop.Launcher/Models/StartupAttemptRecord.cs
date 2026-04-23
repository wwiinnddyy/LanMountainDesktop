using System.Text.Json.Serialization;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Models;

internal enum StartupAttemptState
{
    Pending,
    SoftTimeout,
    DetachedWaiting,
    Succeeded,
    Failed
}

internal sealed class StartupAttemptRecord
{
    [JsonPropertyName("attemptId")]
    public string AttemptId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("hostPid")]
    public int HostPid { get; set; }

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("launchSource")]
    public string LaunchSource { get; set; } = string.Empty;

    [JsonPropertyName("successPolicy")]
    public string SuccessPolicy { get; set; } = string.Empty;

    [JsonPropertyName("lastObservedStage")]
    public StartupStage LastObservedStage { get; set; } = StartupStage.Initializing;

    [JsonPropertyName("lastObservedMessage")]
    public string LastObservedMessage { get; set; } = string.Empty;

    [JsonPropertyName("ipcConnected")]
    public bool IpcConnected { get; set; }

    [JsonPropertyName("state")]
    public StartupAttemptState State { get; set; } = StartupAttemptState.Pending;
}
