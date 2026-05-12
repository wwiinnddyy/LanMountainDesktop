using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.Contracts.Update;
using SettingsUpdateSettingsState = LanMountainDesktop.Services.Settings.UpdateSettingsState;

namespace LanMountainDesktop.Services.Update;

internal static class HostUpdateOrchestratorProvider
{
    private static readonly object Gate = new();
    private static UpdateOrchestrator? _instance;

    public static UpdateOrchestrator GetOrCreate()
    {
        lock (Gate)
        {
            if (_instance is not null)
            {
                return _instance;
            }

            var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
            var githubProvider = new GithubReleaseManifestProvider("wwiinnddyy", "LanMountainDesktop");
            var staticProvider = new PlondsApiManifestProvider("https://api.classisland.tech");
            var compositeProvider = new CompositeManifestProvider(staticProvider, githubProvider);
            var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var downloadEngine = new UpdateDownloadEngine(compositeProvider, new ResumableDownloadService(httpClient));
            var installGateway = new UpdateInstallGateway();
            var stateStore = new UpdateStateStore(settingsFacade);
            _instance = new UpdateOrchestrator(compositeProvider, downloadEngine, installGateway, stateStore);
            return _instance;
        }
    }
}

public sealed class UpdateOrchestrator : IDisposable
{
    private readonly IUpdateManifestProvider _manifestProvider;
    private readonly UpdateDownloadEngine _downloadEngine;
    private readonly UpdateInstallGateway _installGateway;
    private readonly UpdateStateStore _stateStore;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _cancellationSync = new();
    private CancellationTokenSource? _activeOperationCts;
    private bool _disposed;

    internal UpdateOrchestrator(
        IUpdateManifestProvider manifestProvider,
        UpdateDownloadEngine downloadEngine,
        UpdateInstallGateway installGateway,
        UpdateStateStore stateStore)
    {
        _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
        _downloadEngine = downloadEngine ?? throw new ArgumentNullException(nameof(downloadEngine));
        _installGateway = installGateway ?? throw new ArgumentNullException(nameof(installGateway));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));

        _stateStore.PhaseChanged += OnPhaseChanged;
        _stateStore.ProgressChanged += OnProgressChanged;
    }

    public UpdatePhase CurrentPhase => _stateStore.CurrentPhase;

    public UpdateManifest? CurrentManifest => _stateStore.PendingManifest;

    public event Action<UpdatePhase>? PhaseChanged;
    public event Action<UpdateProgressReport>? ProgressChanged;

    private CancellationToken RegisterOperationCancellation(CancellationToken ct)
    {
        lock (_cancellationSync)
        {
            _activeOperationCts?.Dispose();
            _activeOperationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return _activeOperationCts.Token;
        }
    }

    private void ClearOperationCancellation()
    {
        lock (_cancellationSync)
        {
            _activeOperationCts?.Dispose();
            _activeOperationCts = null;
        }
    }

    public async Task<UpdateCheckReport> CheckAsync(CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        var operationToken = RegisterOperationCancellation(ct);
        try
        {
            if (!CurrentPhase.CanCheck())
            {
                return new UpdateCheckReport(
                    false, null, null, null, null, null, null, null, null,
                    $"Cannot check in phase {CurrentPhase}.");
            }

            _stateStore.TransitionTo(UpdatePhase.Checking);

            var settings = _stateStore.GetSettings();
            var channel = UpdateSettingsValues.NormalizeChannel(settings.UpdateChannel);
            var currentVersionText = _stateStore.GetSettings().PendingUpdateVersion
                ?? AppVersionProvider.ResolveForCurrentProcess().Version;

            if (!TryParseVersion(currentVersionText, out var currentVersion))
            {
                _stateStore.TransitionTo(UpdatePhase.Failed);
                _stateStore.RecordFailure($"Invalid current version text: {currentVersionText}");
                return new UpdateCheckReport(
                    false,
                    null,
                    currentVersionText,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    $"Invalid current version text: {currentVersionText}");
            }

            UpdateManifest? manifest;
            try
            {
                manifest = await _manifestProvider.GetLatestAsync(
                    channel,
                    LanMountainDesktop.Services.PlondsStaticUpdateService.ResolveCurrentPlatform(),
                    currentVersion,
                    operationToken);
            }
            catch (OperationCanceledException)
            {
                _stateStore.TransitionTo(UpdatePhase.Idle);
                throw;
            }
            catch (Exception ex)
            {
                _stateStore.TransitionTo(UpdatePhase.Failed);
                _stateStore.RecordFailure(ex.Message);
                return new UpdateCheckReport(false, null, currentVersionText, null, null, null, null, null, null, ex.Message);
            }

            if (manifest is null)
            {
                _stateStore.TransitionTo(UpdatePhase.Checked);
                return new UpdateCheckReport(
                    false, null, currentVersionText, null, null, null, null, null, null, null);
            }

            _stateStore.PendingManifest = manifest;
            _stateStore.TransitionTo(UpdatePhase.Checked);

            long? totalBytes = manifest.IsDelta ? manifest.EstimatedDeltaBytes : null;
            long? installerBytes = manifest.InstallerMirrors?.Count > 0
                ? manifest.InstallerMirrors[0].Size
                : null;

            return new UpdateCheckReport(
                true,
                manifest.ToVersion,
                currentVersionText,
                manifest.Kind,
                manifest.DistributionId,
                manifest.Channel,
                manifest.PublishedAt,
                totalBytes,
                installerBytes,
                null);
        }
        finally
        {
            ClearOperationCancellation();
            _operationGate.Release();
        }
    }

    public async Task<DownloadResult> DownloadAsync(CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        var operationToken = RegisterOperationCancellation(ct);
        try
        {
            if (CurrentPhase is not (UpdatePhase.Checked or UpdatePhase.PausedDownloading))
            {
                return new DownloadResult(false, null, $"Cannot download in phase {CurrentPhase}.", false);
            }

            var manifest = _stateStore.PendingManifest;
            if (manifest is null)
            {
                return new DownloadResult(false, null, "No manifest available for download.", false);
            }

            _stateStore.TransitionTo(UpdatePhase.Downloading);

            var settings = _stateStore.GetSettings();
            var maxThreads = UpdateSettingsValues.NormalizeDownloadThreads(settings.UpdateDownloadThreads);
            var launcherRoot = UpdatePaths.ResolveLauncherRoot(AppContext.BaseDirectory);

            var downloadProgress = new Progress<DownloadProgressReport>(p =>
            {
                var overallFraction = manifest.IsDelta
                    ? (double)p.FilesCompleted / Math.Max(1, p.FilesTotal)
                    : p.OverallFraction;

                ProgressChanged?.Invoke(new UpdateProgressReport(
                    UpdatePhase.Downloading,
                    $"Downloading {p.CurrentFile}",
                    overallFraction,
                    p,
                    null));
            });

            try
            {
                DownloadResult result;

                if (manifest.IsDelta)
                {
                    var incomingDir = UpdatePaths.GetIncomingDirectory(launcherRoot);
                    var objectsDir = UpdatePaths.GetObjectsDirectory(launcherRoot);
                    result = await _downloadEngine.DownloadPayloadAsync(
                        manifest,
                        incomingDir,
                        objectsDir,
                        maxThreads,
                        downloadProgress,
                        operationToken);
                }
                else
                {
                    var fileName = $"{manifest.DistributionId}-{manifest.ToVersion}-installer.exe";
                    var destinationPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "LanMountainDesktop",
                        "Updates",
                        fileName);
                    result = await _downloadEngine.DownloadFullInstallerAsync(
                        manifest,
                        destinationPath,
                        maxThreads,
                        downloadProgress,
                        operationToken);
                }

                if (result.Success)
                {
                    _stateStore.TransitionTo(UpdatePhase.Downloaded);

                    var state = _stateStore.GetSettings();
                    _stateStore.SaveSettings(state with
                    {
                        PendingUpdateInstallerPath = result.FilePath,
                        PendingUpdateVersion = manifest.ToVersion,
                        PendingUpdatePublishedAtUtcMs = manifest.PublishedAt.ToUnixTimeMilliseconds(),
                        PendingUpdateSha256 = null,
                        LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });

                    var payloadKind = manifest.IsDelta ? "delta" : "full";
                    DeploymentLockService.WriteLock(launcherRoot, new DeploymentLock(
                        SchemaVersion: 1,
                        Kind: payloadKind,
                        TargetVersion: manifest.ToVersion,
                        PayloadPath: result.FilePath ?? string.Empty,
                        PayloadSha256: null,
                        CreatedAtUtc: DateTimeOffset.UtcNow));

                    AppLogger.Info("UpdateOrchestrator", $"Update downloaded successfully: {manifest.ToVersion}");
                }
                else
                {
                    _stateStore.TransitionTo(UpdatePhase.Failed);
                    _stateStore.RecordFailure(result.ErrorMessage ?? "Download failed");
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                if (CurrentPhase != UpdatePhase.PausedDownloading)
                {
                    _stateStore.TransitionTo(UpdatePhase.Idle);
                }

                throw;
            }
            catch (Exception ex)
            {
                _stateStore.TransitionTo(UpdatePhase.Failed);
                _stateStore.RecordFailure(ex.Message);
                return new DownloadResult(false, null, ex.Message, false);
            }
        }
        finally
        {
            ClearOperationCancellation();
            _operationGate.Release();
        }
    }

    public async Task<InstallResult> InstallAsync(CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        var operationToken = RegisterOperationCancellation(ct);
        try
        {
            if (!CurrentPhase.CanInstall())
            {
                return new InstallResult(false, $"Cannot install in phase {CurrentPhase}.", false, "invalid_phase");
            }

            var manifest = _stateStore.PendingManifest;
            if (manifest is null)
            {
                return new InstallResult(false, "No manifest available for install.", false, "staging_incomplete");
            }

            _stateStore.TransitionTo(UpdatePhase.Installing);

            var launcherRoot = UpdatePaths.ResolveLauncherRoot(AppContext.BaseDirectory);

            var installProgress = new Progress<InstallProgressReport>(p =>
            {
                var fraction = p.FilesTotal > 0 ? (double)p.FilesCompleted / p.FilesTotal : p.ProgressPercent / 100.0;
                ProgressChanged?.Invoke(new UpdateProgressReport(
                    UpdatePhase.Installing,
                    p.Message,
                    fraction,
                    null,
                    p));
            });

            try
            {
                var result = await _installGateway.InstallAsync(
                    manifest.Kind,
                    launcherRoot,
                    installProgress,
                    operationToken);

                if (result.Success)
                {
                    _stateStore.TransitionTo(UpdatePhase.Installed);
                    _stateStore.RecordSuccess(manifest.ToVersion);
                    AppLogger.Info("UpdateOrchestrator", $"Update install initiated: {manifest.ToVersion}");
                }
                else
                {
                    _stateStore.TransitionTo(UpdatePhase.Failed);
                    _stateStore.RecordFailure(result.ErrorMessage ?? "Install failed");
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                if (CurrentPhase != UpdatePhase.PausedInstalling)
                {
                    _stateStore.TransitionTo(UpdatePhase.Idle);
                }

                throw;
            }
            catch (Exception ex)
            {
                _stateStore.TransitionTo(UpdatePhase.Failed);
                _stateStore.RecordFailure(ex.Message);
                return new InstallResult(false, ex.Message, false, "install_exception");
            }
        }
        finally
        {
            ClearOperationCancellation();
            _operationGate.Release();
        }
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        await _operationGate.WaitAsync(ct);
        var operationToken = RegisterOperationCancellation(ct);
        try
        {
            operationToken.ThrowIfCancellationRequested();

            if (!CurrentPhase.CanRollback())
            {
                return;
            }

            _stateStore.TransitionTo(UpdatePhase.RollingBack);

            try
            {
                var launcherPath = LauncherPathResolver.ResolveLauncherExecutablePath();
                if (!string.IsNullOrWhiteSpace(launcherPath) && File.Exists(launcherPath))
                {
                    var launcherRoot = Path.GetDirectoryName(launcherPath)!;
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = launcherPath,
                        Arguments = $"rollback --app-root \"{launcherRoot}\"",
                        UseShellExecute = false,
                        WorkingDirectory = launcherRoot
                    };

                    System.Diagnostics.Process.Start(startInfo);
                    AppLogger.Info("UpdateOrchestrator", "Launched Launcher for rollback.");
                }

                _stateStore.TransitionTo(UpdatePhase.RolledBack);
            }
            catch (OperationCanceledException)
            {
                _stateStore.TransitionTo(UpdatePhase.Idle);
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("UpdateOrchestrator", $"Rollback failed: {ex.Message}");
                _stateStore.TransitionTo(UpdatePhase.Failed);
            }
        }
        finally
        {
            ClearOperationCancellation();
            _operationGate.Release();
        }
    }

    public Task CancelAsync()
    {
        if (!CurrentPhase.CanCancel())
        {
            return Task.CompletedTask;
        }

        lock (_cancellationSync)
        {
            _activeOperationCts?.Cancel();
        }

        _stateStore.TransitionTo(UpdatePhase.Idle);

        var launcherRoot = UpdatePaths.ResolveLauncherRoot(AppContext.BaseDirectory);
        CleanupIncomingArtifacts(launcherRoot);
        DeploymentLockService.ClearLock(launcherRoot);

        var state = _stateStore.GetSettings();
        _stateStore.SaveSettings(state with
        {
            PendingUpdateInstallerPath = null,
            PendingUpdateVersion = null,
            PendingUpdateSha256 = null
        });

        AppLogger.Info("UpdateOrchestrator", "Cancellation requested for active update operation.");
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (!CurrentPhase.CanPause())
        {
            return Task.CompletedTask;
        }

        var pausedPhase = CurrentPhase switch
        {
            UpdatePhase.Downloading => UpdatePhase.PausedDownloading,
            UpdatePhase.Installing => UpdatePhase.PausedInstalling,
            _ => UpdatePhase.Idle
        };

        _stateStore.TransitionTo(pausedPhase);

        lock (_cancellationSync)
        {
            _activeOperationCts?.Cancel();
        }

        AppLogger.Info("UpdateOrchestrator", $"Pause requested in phase {pausedPhase}.");
        return Task.CompletedTask;
    }

    public async Task<DownloadResult> ResumeAsync(CancellationToken ct)
    {
        return CurrentPhase switch
        {
            UpdatePhase.PausedDownloading => await DownloadAsync(ct),
            UpdatePhase.PausedInstalling => await ResumeInstallAsync(ct),
            _ => new DownloadResult(false, null, $"Cannot resume in phase {CurrentPhase}.", false)
        };
    }

    private async Task<DownloadResult> ResumeInstallAsync(CancellationToken ct)
    {
        _stateStore.TransitionTo(UpdatePhase.Recovering);
        var installResult = await InstallAsync(ct);
        if (installResult.Success)
        {
            return new DownloadResult(true, null, null, false);
        }

        return new DownloadResult(false, null, installResult.ErrorMessage ?? installResult.ErrorCode ?? "Install resume failed.", false);
    }

    public async Task AutoCheckIfEnabledAsync(CancellationToken ct)
    {
        var settings = _stateStore.GetSettings();
        var mode = UpdateSettingsValues.NormalizeMode(settings.UpdateMode);

        if (string.Equals(mode, UpdateSettingsValues.ModeManual, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await CheckAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateOrchestrator", "Automatic update check failed.", ex);
        }
    }

    public bool TryApplyOnExit()
    {
        var settings = _stateStore.GetSettings();
        var mode = UpdateSettingsValues.NormalizeMode(settings.UpdateMode);

        if (!string.Equals(mode, UpdateSettingsValues.ModeSilentOnExit, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var manifest = _stateStore.PendingManifest;
        if (manifest is null)
        {
            return false;
        }

        var launcherRoot = UpdatePaths.ResolveLauncherRoot(AppContext.BaseDirectory);

        if (manifest.IsDelta)
        {
            AppLogger.Info("UpdateOrchestrator", "Delta update pending. Launching Launcher to apply on exit.");
            var launcherPath = LauncherPathResolver.ResolveLauncherExecutablePath();
            if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            {
                return false;
            }

            try
            {
                var resolvedRoot = Path.GetDirectoryName(launcherPath)!;
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = launcherPath,
                    Arguments = $"apply-update --app-root \"{resolvedRoot}\" --launch-source apply-update",
                    UseShellExecute = false,
                    WorkingDirectory = resolvedRoot
                };

                System.Diagnostics.Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("UpdateOrchestrator", $"Failed to launch Launcher on exit: {ex.Message}");
                return false;
            }
        }

        var installerPath = settings.PendingUpdateInstallerPath?.Trim();
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            return false;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                WorkingDirectory = Path.GetDirectoryName(installerPath)!,
                UseShellExecute = true,
                Verb = System.OperatingSystem.IsWindows() ? "runas" : string.Empty,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
            };

            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateOrchestrator", $"Failed to launch installer on exit: {ex.Message}");
            return false;
        }
    }

    private static void CleanupIncomingArtifacts(string launcherRoot)
    {
        var incomingDir = UpdatePaths.GetIncomingDirectory(launcherRoot);

        foreach (var path in new[]
                 {
                     Path.Combine(incomingDir, UpdatePaths.GetLegacyFileMapName()),
                     Path.Combine(incomingDir, UpdatePaths.GetLegacySignatureName()),
                     Path.Combine(incomingDir, UpdatePaths.GetLegacyArchiveName()),
                     Path.Combine(incomingDir, UpdatePaths.GetPlondsFileMapName()),
                     Path.Combine(incomingDir, UpdatePaths.GetPlondsSignatureName()),
                     Path.Combine(incomingDir, UpdatePaths.GetPlondsUpdateMetadataName()),
                     UpdatePaths.GetDownloadMarkerPath(launcherRoot)
                 })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        try
        {
            var objectsDir = UpdatePaths.GetObjectsDirectory(launcherRoot);
            if (Directory.Exists(objectsDir))
            {
                Directory.Delete(objectsDir, true);
            }
        }
        catch
        {
        }
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimStart('v', 'V');
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex >= 0)
        {
            normalized = normalized[..dashIndex];
        }

        var plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
        {
            normalized = normalized[..plusIndex];
        }

        if (!Version.TryParse(normalized, out var parsed))
        {
            return false;
        }

        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }

    private void OnPhaseChanged(UpdatePhase phase)
    {
        PhaseChanged?.Invoke(phase);
    }

    private void OnProgressChanged(UpdateProgressReport report)
    {
        ProgressChanged?.Invoke(report);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stateStore.PhaseChanged -= OnPhaseChanged;
        _stateStore.ProgressChanged -= OnProgressChanged;
        lock (_cancellationSync)
        {
            _activeOperationCts?.Dispose();
            _activeOperationCts = null;
        }
        _operationGate.Dispose();
    }
}
