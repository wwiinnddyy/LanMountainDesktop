using System.Diagnostics;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Startup;

internal enum HostStartMode
{
    ShellExecute,
    Direct
}

internal sealed record HostStartAttempt(
    HostStartMode StartMode,
    bool ProcessCreated,
    Process? Process,
    bool ExitedEarly,
    int? ExitCode,
    string? FailureReason,
    string? PackageRoot,
    string? WorkingDirectory,
    string? Arguments)
{
    public int? ProcessId => Process?.Id;

    public static HostStartAttempt Started(HostStartMode startMode, Process process, HostLaunchPlan plan) =>
        new(
            startMode,
            true,
            process,
            false,
            null,
            null,
            plan.PackageRoot,
            plan.WorkingDirectory,
            HostLaunchPlanBuilder.FormatArgumentsForLog(plan.Arguments));

    public static HostStartAttempt EarlyExit(HostStartMode startMode, Process process, int exitCode, HostLaunchPlan plan) =>
        new(
            startMode,
            true,
            process,
            true,
            exitCode,
            null,
            plan.PackageRoot,
            plan.WorkingDirectory,
            HostLaunchPlanBuilder.FormatArgumentsForLog(plan.Arguments));

    public static HostStartAttempt StartFailed(HostStartMode startMode, string failureReason, HostLaunchPlan? plan = null) =>
        new(
            startMode,
            false,
            null,
            false,
            null,
            failureReason,
            plan?.PackageRoot,
            plan?.WorkingDirectory,
            plan is null ? null : HostLaunchPlanBuilder.FormatArgumentsForLog(plan.Arguments));
}

internal sealed record HostLaunchOutcome(
    LauncherResult Result,
    Process? Process,
    LauncherResult? ImmediateResult,
    Dictionary<string, string> Details)
{
    public static HostLaunchOutcome FromResult(LauncherResult result) =>
        new(result, null, result.Success ? result : null, result.Details);

    public static HostLaunchOutcome FromImmediateResult(LauncherResult result) =>
        new(result, null, result, result.Details);

    public static HostLaunchOutcome FromProcess(Process process, LauncherResult result, Dictionary<string, string> details) =>
        new(result, process, null, details);
}
