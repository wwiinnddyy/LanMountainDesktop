using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Oobe;

internal sealed class OobeStateService
{
    private const int CurrentSchemaVersion = 1;

    private readonly string _appRoot;
    private readonly string? _stateRootOverride;
    private readonly string _stateDirectory;
    private readonly string _statePath;
    private readonly IReadOnlyList<string> _legacyStatePaths;
    private readonly IReadOnlyList<string> _legacyMarkerPaths;
    private readonly LauncherExecutionSnapshot _executionSnapshot;

    public OobeStateService(
        string appRoot,
        string? stateRootOverride = null,
        LauncherExecutionSnapshot? executionSnapshot = null)
    {
        _appRoot = Path.GetFullPath(appRoot);
        _stateRootOverride = string.IsNullOrWhiteSpace(stateRootOverride)
            ? null
            : Path.GetFullPath(stateRootOverride);
        _executionSnapshot = executionSnapshot ?? LauncherExecutionContext.Capture();

        var stateRoot = ResolveCurrentStateRoot();
        (_stateDirectory, _statePath) = BuildStatePaths(stateRoot);

        _legacyStatePaths = BuildLegacyPaths("oobe-state.json");
        _legacyMarkerPaths = BuildLegacyPaths("first_run_completed");
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
        return MarkCompleted(context, null);
    }

    public OobeCompletionResult MarkCompleted(CommandContext context, string? stateRoot)
    {
        try
        {
            var (stateDirectory, statePath) = BuildStatePaths(
                string.IsNullOrWhiteSpace(stateRoot) ? ResolveCurrentStateRoot() : Path.GetFullPath(stateRoot));
            Directory.CreateDirectory(stateDirectory);
            var payload = new OobeStateFile
            {
                SchemaVersion = CurrentSchemaVersion,
                CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                UserName = _executionSnapshot.UserName,
                UserSid = _executionSnapshot.UserSid,
                LaunchSource = context.LaunchSource
            };

            var tempPath = Path.Combine(stateDirectory, $"oobe-state.{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(payload, AppJsonContext.Default.OobeStateFile);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, statePath, overwrite: true);
            TryDeleteLegacyMarker();

            Logger.Info(
                $"OOBE completion persisted. LaunchSource='{context.LaunchSource}'; StatePath='{statePath}'; " +
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
                return EvaluateStateFile(context, _statePath, migratedLegacyState: false);
            }

            foreach (var legacyStatePath in _legacyStatePaths)
            {
                if (File.Exists(legacyStatePath))
                {
                    var decision = EvaluateStateFile(context, legacyStatePath, migratedLegacyState: true);
                    if (decision.Status == OobeStateStatus.Completed)
                    {
                        _ = MarkCompleted(context);
                    }

                    return decision;
                }
            }

            foreach (var legacyMarkerPath in _legacyMarkerPaths)
            {
                if (File.Exists(legacyMarkerPath))
                {
                    migratedLegacyMarker = TryMigrateLegacyMarker(context);
                    return BuildDecision(context, OobeStateStatus.Completed, shouldShowOobe: false, usedLegacyMarker: true, migratedLegacyMarker: migratedLegacyMarker);
                }
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

    private OobeLaunchDecision EvaluateStateFile(CommandContext context, string statePath, bool migratedLegacyState)
    {
        using var stream = File.OpenRead(statePath);
        var state = JsonSerializer.Deserialize(stream, AppJsonContext.Default.OobeStateFile);
        if (state is null || state.SchemaVersion <= 0 || string.IsNullOrWhiteSpace(state.CompletedAtUtc))
        {
            return BuildUnavailableDecision(context, "OOBE state file is invalid.");
        }

        return BuildDecision(context, OobeStateStatus.Completed, shouldShowOobe: false, migratedLegacyMarker: migratedLegacyState);
    }

    private void TryDeleteLegacyMarker()
    {
        foreach (var legacyMarkerPath in _legacyMarkerPaths)
        {
            try
            {
                if (File.Exists(legacyMarkerPath))
                {
                    File.Delete(legacyMarkerPath);
                }
            }
            catch
            {
            }
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

    private string ResolveCurrentStateRoot()
    {
        return _stateRootOverride ?? ResolveStateRoot(_appRoot);
    }

    private static (string StateDirectory, string StatePath) BuildStatePaths(string stateRoot)
    {
        var stateDirectory = Path.Combine(Path.GetFullPath(stateRoot), "Launcher", "state");
        return (stateDirectory, Path.Combine(stateDirectory, "oobe-state.json"));
    }

    private IReadOnlyList<string> BuildLegacyPaths(string fileName)
    {
        var roots = new List<string>();
        if (_stateRootOverride is not null)
        {
            roots.Add(_stateRootOverride);
        }
        else
        {
            roots.Add(ResolveDefaultSystemStateRoot());
            roots.Add(_appRoot);
            try
            {
                roots.Add(ResolveCurrentStateRoot());
            }
            catch
            {
            }
        }

        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.Combine(Path.GetFullPath(root), ".launcher", "state", fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private static string ResolveDefaultSystemStateRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            return string.Empty;
        }

        return Path.Combine(appData, "LanMountainDesktop");
    }
}
