using System.Diagnostics;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Startup;

internal enum LaunchPhaseStatus
{
    Continue,
    Completed
}

internal sealed record LaunchPhaseResult(LaunchPhaseStatus Status, LauncherResult? Result = null);

internal interface ILaunchPhase
{
    string Name { get; }

    Task<LaunchPhaseResult> ExecuteAsync(LaunchContext context, CancellationToken cancellationToken = default);
}

internal sealed class LaunchContext
{
    public required CommandContext CommandContext { get; init; }
    public required DeploymentLocator DeploymentLocator { get; init; }
    public required OobeStateService OobeStateService { get; init; }
    public required StartupAttemptRegistry StartupAttemptRegistry { get; init; }
    public LauncherCoordinatorIpcServer? CoordinatorIpcServer { get; init; }
    public required DataLocationResolver DataLocationResolver { get; init; }
    public required IReadOnlyList<IOobeStep> OobeSteps { get; init; }

    public SplashWindow SplashWindow { get; set; } = null!;
    public LoadingDetailsWindow? LoadingDetailsWindow { get; set; }
    public ISplashStageReporter Reporter { get; set; } = null!;
    public LanMountainDesktopIpcClient IpcClient { get; set; } = null!;
    public StartupSuccessTracker SuccessTracker { get; set; } = null!;
    public TaskCompletionSource<StartupSuccessState> SuccessTcs { get; set; } = null!;
    public TaskCompletionSource<string> ActivationFailedTcs { get; set; } = null!;
    public LoadingStateMessage LoadingState { get; set; }
    public Dictionary<string, string> LauncherContextDetails { get; set; } = [];
    public OobeLaunchDecision OobeDecision { get; set; } = null!;

    public StartupStage LastStage { get; set; } = StartupStage.Initializing;
    public string LastStageMessage { get; set; } = "launcher-started";
    public string ActivationFailureReason { get; set; } = string.Empty;
    public bool IpcConnected { get; set; }
    public bool SoftTimeoutShown { get; set; }
    public bool AttachedToExistingAttempt { get; set; }
    public bool WindowsClosingByOrchestrator { get; set; }
    public StartupAttemptRecord? TrackedAttempt { get; set; }
    public PublicShellStatus? ShellStatus { get; set; }
    public HostLaunchOutcome? LaunchOutcome { get; set; }

    public Action<bool?, bool, bool> PublishCoordinatorStatus { get; set; } = static (_, _, _) => { };
    public EventHandler? SplashClosedHandler { get; set; }
}

internal sealed class LaunchPipeline
{
    private readonly IReadOnlyList<ILaunchPhase> _phases;

    public LaunchPipeline(IEnumerable<ILaunchPhase> phases)
    {
        _phases = phases.ToList();
    }

    public async Task<LauncherResult> ExecuteAsync(LaunchContext context, CancellationToken cancellationToken = default)
    {
        foreach (var phase in _phases)
        {
            Logger.Info($"Launch pipeline entering phase '{phase.Name}'.");
            var phaseResult = await phase.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            if (phaseResult.Status == LaunchPhaseStatus.Completed)
            {
                return phaseResult.Result ?? LaunchResultBuilder.BuildFailure(
                    "launch",
                    "phase_completed_without_result",
                    $"Launch phase '{phase.Name}' completed without a result.");
            }
        }

        return LaunchResultBuilder.BuildFailure(
            "launch",
            "pipeline_incomplete",
            "Launch pipeline finished without producing a result.");
    }
}

internal static class LaunchResultBuilder
{
    public static LauncherResult Build(
        bool success,
        string stage,
        string code,
        string message,
        Dictionary<string, string>? details = null,
        string? errorMessage = null)
    {
        Logger.Info($"Launcher result prepared. Success={success}; Stage='{stage}'; Code='{code}'.");
        return new LauncherResult
        {
            Success = success,
            Stage = stage,
            Code = code,
            Message = message,
            ErrorMessage = errorMessage,
            Details = details ?? []
        };
    }

    public static LauncherResult BuildFailure(string stage, string code, string message) =>
        Build(false, stage, code, message);

    public static LauncherResult WithAdditionalDetails(LauncherResult result, Dictionary<string, string> details) =>
        new()
        {
            Success = result.Success,
            Stage = result.Stage,
            Code = result.Code,
            Message = result.Message,
            CurrentVersion = result.CurrentVersion,
            TargetVersion = result.TargetVersion,
            RolledBackTo = result.RolledBackTo,
            Details = MergeDetails(details, result.Details),
            InstalledPackagePath = result.InstalledPackagePath,
            ManifestId = result.ManifestId,
            ManifestName = result.ManifestName,
            ErrorMessage = result.ErrorMessage
        };

    public static Dictionary<string, string> BuildLauncherContextDetails(
        CommandContext context,
        OobeLaunchDecision oobeDecision,
        string appRoot) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["command"] = context.Command,
            ["launchSource"] = context.LaunchSource,
            ["isGuiMode"] = context.IsGuiCommand.ToString(),
            ["isDebugMode"] = context.IsDebugMode.ToString(),
            ["isElevated"] = oobeDecision.IsElevated.ToString(),
            ["resolvedAppRoot"] = appRoot,
            ["oobeStatePath"] = oobeDecision.StatePath,
            ["oobeStateStatus"] = oobeDecision.Status.ToString(),
            ["oobeDecision"] = oobeDecision.ShouldShowOobe ? "show" : "skip",
            ["oobeSuppressionReason"] = oobeDecision.SuppressionReason,
            ["oobeResultCode"] = oobeDecision.ResultCode,
            ["userSid"] = oobeDecision.UserSid ?? string.Empty,
            ["usedLegacyOobeMarker"] = oobeDecision.UsedLegacyMarker.ToString(),
            ["migratedLegacyOobeMarker"] = oobeDecision.MigratedLegacyMarker.ToString(),
            ["oobeStateError"] = oobeDecision.ErrorMessage
        };

    public static Dictionary<string, string> MergeDetails(
        Dictionary<string, string> left,
        Dictionary<string, string> right)
    {
        var merged = new Dictionary<string, string>(left, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in right)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    public static bool TryGetLiveProcess(int processId, out Process? process)
    {
        process = null;
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }
}
