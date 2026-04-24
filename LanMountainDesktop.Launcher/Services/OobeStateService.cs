using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class OobeStateService
{
    private const int CurrentSchemaVersion = 1;

    private readonly string _stateDirectory;
    private readonly string _statePath;
    private readonly string _legacyMarkerPath;
    private readonly LauncherExecutionSnapshot _executionSnapshot;

    public OobeStateService(
        string appRoot,
        string? stateRootOverride = null,
        LauncherExecutionSnapshot? executionSnapshot = null)
    {
        _ = Path.GetFullPath(appRoot);
        _executionSnapshot = executionSnapshot ?? LauncherExecutionContext.Capture();

        var stateRoot = string.IsNullOrWhiteSpace(stateRootOverride)
            ? ResolveStateRoot(appRoot)
            : Path.GetFullPath(stateRootOverride);
        _stateDirectory = Path.Combine(stateRoot, ".launcher", "state");
        _statePath = Path.Combine(_stateDirectory, "oobe-state.json");
        _legacyMarkerPath = Path.Combine(_stateDirectory, "first_run_completed");
    }

    public OobeLaunchDecision Evaluate(CommandContext context)
    {
        var decision = EvaluateCore(context);
        Logger.Info(
            $"OOBE decision evaluated. LaunchSource='{decision.LaunchSource}'; Status='{decision.Status}'; " +
            $"ShouldShow={decision.ShouldShowOobe}; IsElevated={decision.IsElevated}; " +
            $"StatePath='{decision.StatePath}'; SuppressionReason='{decision.SuppressionReason}'; " +
            $"ResultCode='{decision.ResultCode}'; UserSid='{decision.UserSid ?? string.Empty}'.");
        return decision;
    }

    public OobeCompletionResult MarkCompleted(CommandContext context)
    {
        try
        {
            Directory.CreateDirectory(_stateDirectory);
            var payload = new OobeStateFile
            {
                SchemaVersion = CurrentSchemaVersion,
                CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                UserName = _executionSnapshot.UserName,
                UserSid = _executionSnapshot.UserSid,
                LaunchSource = context.LaunchSource
            };

            var tempPath = Path.Combine(_stateDirectory, $"oobe-state.{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(payload, AppJsonContext.Default.OobeStateFile);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _statePath, overwrite: true);
            TryDeleteLegacyMarker();

            Logger.Info(
                $"OOBE completion persisted. LaunchSource='{context.LaunchSource}'; StatePath='{_statePath}'; " +
                $"UserSid='{_executionSnapshot.UserSid ?? string.Empty}'.");

            return new OobeCompletionResult
            {
                Success = true,
                ResultCode = "ok"
            };
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"Failed to persist OOBE state. LaunchSource='{context.LaunchSource}'; StatePath='{_statePath}'; " +
                $"Error='{ex.Message}'.");
            return new OobeCompletionResult
            {
                Success = false,
                ResultCode = "oobe_state_unavailable",
                ErrorMessage = ex.Message
            };
        }
    }

    private OobeLaunchDecision EvaluateCore(CommandContext context)
    {
        if (string.Equals(context.LaunchSource, "debug-preview", StringComparison.OrdinalIgnoreCase))
        {
            return BuildSuppressedDecision(context, "debug_preview", "oobe_suppressed_debug_preview");
        }

        if (context.IsMaintenanceCommand)
        {
            return BuildSuppressedDecision(context, "maintenance", "oobe_suppressed_maintenance");
        }

        try
        {
            var migratedLegacyMarker = false;
            if (File.Exists(_statePath))
            {
                using var stream = File.OpenRead(_statePath);
                var state = JsonSerializer.Deserialize(stream, AppJsonContext.Default.OobeStateFile);
                if (state is null || state.SchemaVersion <= 0 || string.IsNullOrWhiteSpace(state.CompletedAtUtc))
                {
                    return BuildUnavailableDecision(context, "OOBE state file is invalid.");
                }

                return BuildDecision(context, OobeStateStatus.Completed, shouldShowOobe: false, migratedLegacyMarker: false);
            }

            if (File.Exists(_legacyMarkerPath))
            {
                migratedLegacyMarker = TryMigrateLegacyMarker(context);
                return BuildDecision(context, OobeStateStatus.Completed, shouldShowOobe: false, usedLegacyMarker: true, migratedLegacyMarker: migratedLegacyMarker);
            }

            if (_executionSnapshot.IsElevated)
            {
                return BuildSuppressedDecision(context, "elevated", "oobe_suppressed_elevated");
            }

            if (string.Equals(context.LaunchSource, "postinstall", StringComparison.OrdinalIgnoreCase))
            {
                return BuildDecision(context, OobeStateStatus.FirstRun, shouldShowOobe: true);
            }

            return BuildDecision(context, OobeStateStatus.FirstRun, shouldShowOobe: true);
        }
        catch (Exception ex)
        {
            return BuildUnavailableDecision(context, ex.Message);
        }
    }

    private bool TryMigrateLegacyMarker(CommandContext context)
    {
        var result = MarkCompleted(context);
        return result.Success;
    }

    private void TryDeleteLegacyMarker()
    {
        try
        {
            if (File.Exists(_legacyMarkerPath))
            {
                File.Delete(_legacyMarkerPath);
            }
        }
        catch
        {
        }
    }

    private OobeLaunchDecision BuildDecision(
        CommandContext context,
        OobeStateStatus status,
        bool shouldShowOobe,
        bool usedLegacyMarker = false,
        bool migratedLegacyMarker = false)
    {
        return new OobeLaunchDecision
        {
            Status = status,
            ShouldShowOobe = shouldShowOobe,
            StatePath = _statePath,
            LaunchSource = context.LaunchSource,
            IsElevated = _executionSnapshot.IsElevated,
            UserName = _executionSnapshot.UserName,
            UserSid = _executionSnapshot.UserSid,
            UsedLegacyMarker = usedLegacyMarker,
            MigratedLegacyMarker = migratedLegacyMarker,
            ResultCode = "ok"
        };
    }

    private OobeLaunchDecision BuildSuppressedDecision(CommandContext context, string reason, string resultCode)
    {
        return new OobeLaunchDecision
        {
            Status = OobeStateStatus.Suppressed,
            ShouldShowOobe = false,
            StatePath = _statePath,
            LaunchSource = context.LaunchSource,
            IsElevated = _executionSnapshot.IsElevated,
            UserName = _executionSnapshot.UserName,
            UserSid = _executionSnapshot.UserSid,
            SuppressionReason = reason,
            ResultCode = resultCode
        };
    }

    private OobeLaunchDecision BuildUnavailableDecision(CommandContext context, string errorMessage)
    {
        return new OobeLaunchDecision
        {
            Status = OobeStateStatus.Unavailable,
            ShouldShowOobe = false,
            StatePath = _statePath,
            LaunchSource = context.LaunchSource,
            IsElevated = _executionSnapshot.IsElevated,
            UserName = _executionSnapshot.UserName,
            UserSid = _executionSnapshot.UserSid,
            ResultCode = "oobe_state_unavailable",
            ErrorMessage = errorMessage
        };
    }

    private static string ResolveStateRoot(string appRoot)
    {
        try
        {
            var resolver = new DataLocationResolver(appRoot);
            return resolver.ResolveDataRoot();
        }
        catch
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                throw new InvalidOperationException("LocalApplicationData is unavailable.");
            }

            return Path.Combine(appData, "LanMountainDesktop");
        }
    }
}
