using System.Text.Json.Serialization;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Models;

internal static class LauncherCoordinatorCommands
{
    public const string Attach = "attach";
    public const string ActivateDesktop = "activate-desktop";
    public const string GetStatus = "get-status";
}

internal sealed class LauncherCoordinatorRequest
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("command")]
    public string Command { get; init; } = LauncherCoordinatorCommands.Attach;

    [JsonPropertyName("launcherPid")]
    public int LauncherPid { get; init; } = Environment.ProcessId;

    [JsonPropertyName("launchSource")]
    public string LaunchSource { get; init; } = string.Empty;

    [JsonPropertyName("successPolicy")]
    public string SuccessPolicy { get; init; } = string.Empty;
}

internal sealed class LauncherCoordinatorResponse
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public LauncherCoordinatorStatus? Status { get; init; }

    [JsonPropertyName("activationResult")]
    public PublicShellActivationResult? ActivationResult { get; init; }
}

internal sealed class LauncherCoordinatorStatus
{
    [JsonPropertyName("attemptId")]
    public string AttemptId { get; init; } = string.Empty;

    [JsonPropertyName("coordinatorPid")]
    public int CoordinatorPid { get; init; } = Environment.ProcessId;

    [JsonPropertyName("hostPid")]
    public int HostPid { get; init; }

    [JsonPropertyName("hostProcessAlive")]
    public bool HostProcessAlive { get; init; }

    [JsonPropertyName("launchSource")]
    public string LaunchSource { get; init; } = string.Empty;

    [JsonPropertyName("successPolicy")]
    public string SuccessPolicy { get; init; } = string.Empty;

    [JsonPropertyName("lastObservedStage")]
    public StartupStage LastObservedStage { get; init; } = StartupStage.Initializing;

    [JsonPropertyName("lastObservedMessage")]
    public string LastObservedMessage { get; init; } = string.Empty;

    [JsonPropertyName("publicIpcConnected")]
    public bool PublicIpcConnected { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("softTimeoutShown")]
    public bool SoftTimeoutShown { get; init; }

    [JsonPropertyName("completed")]
    public bool Completed { get; init; }

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("shellStatus")]
    public PublicShellStatus? ShellStatus { get; init; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
