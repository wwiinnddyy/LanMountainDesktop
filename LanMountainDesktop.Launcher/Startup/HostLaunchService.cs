using System.Diagnostics;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Shell;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Startup;

internal sealed class HostLaunchService
{
    public async Task<HostLaunchOutcome> LaunchAsync(LaunchContext context, bool forceDirectMode = false, string? retryTag = null)
    {
        var commandContext = context.CommandContext;
        var deploymentLocator = context.DeploymentLocator;
        var resolution = deploymentLocator.ResolveHostExecutable(commandContext);
        if (!resolution.Success || string.IsNullOrWhiteSpace(resolution.ResolvedHostPath))
        {
            var (errorResult, selectedPath) = await LaunchUiPresenter.ShowHostNotFoundErrorAsync().ConfigureAwait(false);
            if (errorResult == ErrorWindowResult.Retry)
            {
                if (!string.IsNullOrWhiteSpace(selectedPath) && File.Exists(selectedPath))
                {
                    return await LaunchWithExplicitPathAsync(context, selectedPath, forceDirectMode, retryTag).ConfigureAwait(false);
                }

                return await LaunchAsync(context, forceDirectMode, retryTag).ConfigureAwait(false);
            }

            return HostLaunchOutcome.FromResult(LaunchResultBuilder.Build(
                success: false,
                stage: "launchHost",
                code: "host_not_found",
                message: "LanMountainDesktop host executable was not found.",
                details: BuildResolutionDetails(resolution, null, null, "resolve")));
        }

        return await LaunchWithResolvedPathAsync(context, resolution, forceDirectMode, retryTag).ConfigureAwait(false);
    }

    public static LauncherResult? ValidateDotNetRuntimePrerequisite(
        HostLaunchPlan plan,
        HostResolutionResult resolution,
        DotNetRuntimeProbeOptions? probeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(resolution);

        if (!DotNetRuntimeProbe.IsFrameworkDependentWindowsApp(plan.HostPath))
        {
            return null;
        }

        var runtime = DotNetRuntimeProbe.Probe(probeOptions);
        Logger.Info(
            $"Runtime prerequisite check completed. Available={runtime.IsAvailable}; " +
            $"Architecture={runtime.Architecture}; Message='{runtime.Message}'.");

        if (runtime.IsAvailable)
        {
            return null;
        }

        var details = BuildResolutionDetails(resolution, null, null, "runtime");
        foreach (var pair in runtime.ToDetails())
        {
            details[pair.Key] = pair.Value;
        }

        return LaunchResultBuilder.Build(
            success: false,
            stage: "launchHost",
            code: "dotnet_runtime_missing",
            message: ".NET 10 Desktop Runtime is required before LanMountainDesktop can start.",
            details: details,
            errorMessage: runtime.Message);
    }

    private static Task<HostLaunchOutcome> LaunchWithExplicitPathAsync(
        LaunchContext context,
        string hostPath,
        bool forceDirectMode,
        string? retryTag)
    {
        var resolution = new HostResolutionResult
        {
            Success = true,
            ResolvedHostPath = Path.GetFullPath(hostPath),
            ResolutionSource = "user_selected_path",
            AppRoot = context.DeploymentLocator.GetAppRoot(),
            ExplicitAppRoot = Path.GetDirectoryName(hostPath),
            SearchedPaths = [Path.GetFullPath(hostPath)]
        };

        return LaunchWithResolvedPathAsync(context, resolution, forceDirectMode, retryTag);
    }

    private static async Task<HostLaunchOutcome> LaunchWithResolvedPathAsync(
        LaunchContext context,
        HostResolutionResult resolution,
        bool forceDirectMode,
        string? retryTag)
    {
        var dataRoot = context.DataLocationResolver.ResolveDataRoot();
        var plan = HostLaunchPlanBuilder.Build(context.CommandContext, context.DeploymentLocator, resolution, dataRoot);
        var prerequisiteFailure = ValidateDotNetRuntimePrerequisite(plan, resolution);
        if (prerequisiteFailure is not null)
        {
            return HostLaunchOutcome.FromResult(prerequisiteFailure);
        }

        await EnsureAirAppRuntimeStartedAsync(context.DeploymentLocator.GetAppRoot(), dataRoot).ConfigureAwait(false);

        var hostPath = plan.HostPath;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            EnsureExecutable(hostPath);
        }

        var primaryMode = HostStartMode.Direct;
        var fallbackMode = !forceDirectMode && OperatingSystem.IsWindows()
            ? HostStartMode.ShellExecute
            : (HostStartMode?)null;

        var firstAttempt = await StartHostProcessAsync(plan, primaryMode, retryTag).ConfigureAwait(false);
        if (firstAttempt.ProcessCreated && firstAttempt.Process is not null)
        {
            var firstDetails = BuildResolutionDetails(resolution, firstAttempt, null, null);
            return HostLaunchOutcome.FromProcess(
                firstAttempt.Process,
                LaunchResultBuilder.Build(true, "launchHost", "ok", "Host launched.", firstDetails),
                firstDetails);
        }

        if (fallbackMode is null)
        {
            return BuildOutcomeFromAttempt(resolution, firstAttempt, null);
        }

        Logger.Warn(
            $"Primary host start attempt failed. Retrying with fallback mode '{fallbackMode}'. " +
            $"FailureReason='{firstAttempt.FailureReason ?? "unknown"}'; ExitCode='{firstAttempt.ExitCode?.ToString() ?? "<none>"}'.");

        var secondAttempt = await StartHostProcessAsync(plan, fallbackMode.Value, retryTag).ConfigureAwait(false);
        if (secondAttempt.ProcessCreated && secondAttempt.Process is not null)
        {
            var details = BuildResolutionDetails(resolution, firstAttempt, secondAttempt, null);
            return HostLaunchOutcome.FromProcess(
                secondAttempt.Process,
                LaunchResultBuilder.Build(true, "launchHost", "ok", "Host launched.", details),
                details);
        }

        return BuildOutcomeFromAttempt(resolution, secondAttempt, firstAttempt);
    }

    private static HostLaunchOutcome BuildOutcomeFromAttempt(
        HostResolutionResult resolution,
        HostStartAttempt finalAttempt,
        HostStartAttempt? previousAttempt)
    {
        var details = BuildResolutionDetails(
            resolution,
            previousAttempt ?? finalAttempt,
            previousAttempt is null ? null : finalAttempt,
            !finalAttempt.ProcessCreated
                ? "start"
                : finalAttempt.ExitCode is int finalExitCode && HostActivationPolicy.IsFailedActivationExitCode(finalExitCode)
                    ? "activation"
                    : "early-exit");

        if (!finalAttempt.ProcessCreated)
        {
            return HostLaunchOutcome.FromResult(LaunchResultBuilder.Build(
                false,
                "launchHost",
                "host_start_failed",
                $"Failed to start host using start mode '{finalAttempt.StartMode}'.",
                details));
        }

        if (finalAttempt.ExitCode is not null && HostActivationPolicy.IsSuccessfulActivationExitCode(finalAttempt.ExitCode.Value))
        {
            return HostLaunchOutcome.FromImmediateResult(LaunchResultBuilder.Build(
                true,
                "launch",
                "activation_redirected",
                "Launcher activation was redirected to the existing desktop instance.",
                details));
        }

        if (finalAttempt.ExitCode is not null && HostActivationPolicy.IsFailedActivationExitCode(finalAttempt.ExitCode.Value))
        {
            return HostLaunchOutcome.FromResult(LaunchResultBuilder.Build(
                false,
                "launch",
                "activation_failed",
                $"Host activation handshake failed using start mode '{finalAttempt.StartMode}'.",
                details));
        }

        return HostLaunchOutcome.FromResult(LaunchResultBuilder.Build(
            false,
            "launchHost",
            "host_exited_early",
            $"Host exited early using start mode '{finalAttempt.StartMode}'.",
            details));
    }

    private static async Task EnsureAirAppRuntimeStartedAsync(string appRoot, string? dataRoot)
    {
        Logger.Info("HOST LAUNCH: Attempting to pre-start AirApp Runtime...");
        try
        {
            await new AirAppRuntimeBridge(appRoot, dataRoot).EnsureStartedAsync().ConfigureAwait(false);
            Logger.Info("HOST LAUNCH: AirApp Runtime pre-start completed.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"HOST LAUNCH: AirApp Runtime pre-start failed; Host fallback remains available. Error='{ex.Message}'");
        }
    }

    private static async Task<HostStartAttempt> StartHostProcessAsync(
        HostLaunchPlan plan,
        HostStartMode startMode,
        string? retryTag)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.HostPath,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = startMode == HostStartMode.ShellExecute
        };

        if (startMode == HostStartMode.Direct)
        {
            foreach (var argument in plan.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            foreach (var pair in plan.EnvironmentVariables)
            {
                startInfo.EnvironmentVariables[pair.Key] = pair.Value;
            }
        }
        else
        {
            startInfo.Arguments = HostLaunchPlanBuilder.FormatArgumentsForLog(plan.Arguments);
        }

        try
        {
            Logger.Info($"ATTEMPTING HOST START: Path='{plan.HostPath}'; WorkingDir='{plan.WorkingDirectory}'; Mode='{startMode}'");
            Logger.Info($"  Arguments: {HostLaunchPlanBuilder.FormatArgumentsForLog(plan.Arguments)}");
            Logger.Info($"  File exists: {File.Exists(plan.HostPath)}");
            Logger.Info($"  Working dir exists: {Directory.Exists(plan.WorkingDirectory)}");

            var process = Process.Start(startInfo);
            Logger.Info(
                $"Host launch requested. Mode='{startMode}'; RetryTag='{retryTag ?? "<none>"}'; Path='{plan.HostPath}'; " +
                $"PackageRoot='{plan.PackageRoot}'; WorkingDir='{plan.WorkingDirectory}'; Pid={(process is null ? -1 : process.Id)}; " +
                $"Args='{HostLaunchPlanBuilder.FormatArgumentsForLog(plan.Arguments)}'.");

            if (process is null)
            {
                Logger.Error($"CRITICAL: Process.Start returned null! Path='{plan.HostPath}'; Mode='{startMode}'");
                Console.Error.WriteLine($"[CRITICAL] Process.Start returned null for path: {plan.HostPath}");
                return HostStartAttempt.StartFailed(startMode, "process_start_returned_null", plan);
            }

            // 等待一小段时间，检查进程是否立即退出
            await Task.Delay(500).ConfigureAwait(false);

            if (process.HasExited)
            {
                Logger.Error($"CRITICAL: Host process exited immediately! ExitCode={process.ExitCode}; Path='{plan.HostPath}'");
                Console.Error.WriteLine($"[CRITICAL] Host process exited immediately with code {process.ExitCode}");
                return HostStartAttempt.StartFailed(startMode, $"process_exited_immediately_code_{process.ExitCode}", plan);
            }

            Logger.Info($"Host process started successfully and is running. PID={process.Id}");
            return HostStartAttempt.Started(startMode, process, plan);
        }
        catch (Exception ex)
        {
            Logger.Error($"CRITICAL: Host start exception! Path='{plan.HostPath}'; Mode='{startMode}'; Exception={ex.GetType().Name}; Message='{ex.Message}'", ex);
            Console.Error.WriteLine($"[CRITICAL] Host start failed: {ex.Message}");
            Console.Error.WriteLine($"[CRITICAL] Path: {plan.HostPath}");
            Console.Error.WriteLine($"[CRITICAL] Exception: {ex}");
            return HostStartAttempt.StartFailed(startMode, ex.GetType().Name, plan);
        }
    }

    internal static Dictionary<string, string> BuildResolutionDetails(
        HostResolutionResult resolution,
        HostStartAttempt? firstAttempt,
        HostStartAttempt? secondAttempt,
        string? failureStage)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedAppRoot"] = resolution.AppRoot,
            ["explicitAppRoot"] = resolution.ExplicitAppRoot ?? string.Empty,
            ["resolvedHostPath"] = resolution.ResolvedHostPath ?? string.Empty,
            ["resolutionSource"] = resolution.ResolutionSource ?? string.Empty,
            ["devModeConfigIgnored"] = resolution.DevModeConfigIgnored.ToString(),
            ["searchedPaths"] = string.Join(" | ", resolution.SearchedPaths),
            ["failureStage"] = failureStage ?? string.Empty
        };

        if (firstAttempt is not null)
        {
            details["startMode"] = firstAttempt.StartMode.ToString();
            details["processCreated"] = firstAttempt.ProcessCreated.ToString();
            details["hostPid"] = firstAttempt.ProcessId?.ToString() ?? string.Empty;
            details["packageRoot"] = firstAttempt.PackageRoot ?? string.Empty;
            details["workingDirectory"] = firstAttempt.WorkingDirectory ?? string.Empty;
            details["arguments"] = firstAttempt.Arguments ?? string.Empty;
            details["firstAttemptFailureReason"] = firstAttempt.FailureReason ?? string.Empty;
            details["firstAttemptExitCode"] = firstAttempt.ExitCode?.ToString() ?? string.Empty;
        }

        if (secondAttempt is not null)
        {
            details["fallbackStartMode"] = secondAttempt.StartMode.ToString();
            details["fallbackProcessCreated"] = secondAttempt.ProcessCreated.ToString();
            details["fallbackHostPid"] = secondAttempt.ProcessId?.ToString() ?? string.Empty;
            details["fallbackPackageRoot"] = secondAttempt.PackageRoot ?? string.Empty;
            details["fallbackWorkingDirectory"] = secondAttempt.WorkingDirectory ?? string.Empty;
            details["fallbackArguments"] = secondAttempt.Arguments ?? string.Empty;
            details["fallbackFailureReason"] = secondAttempt.FailureReason ?? string.Empty;
            details["fallbackExitCode"] = secondAttempt.ExitCode?.ToString() ?? string.Empty;
        }

        return details;
    }

    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }
        catch
        {
        }
    }
}
