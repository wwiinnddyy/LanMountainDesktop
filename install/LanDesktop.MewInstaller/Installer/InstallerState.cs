using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Aprillz.MewUI;
using LanDesktopPLONDS.Installer.Localization;
using LanDesktopPLONDS.Installer.Models;
using LanDesktopPLONDS.Installer.Services;
using LanMountainDesktop.Shared.Contracts.Privacy;
using LanMountainDesktop.Shared.Threading;

namespace LanDesktop.MewInstaller.Installer;

/// <summary>
/// MewUI 安装向导的可观察状态机。
/// 逻辑自 PLONDS 的 MainWindowViewModel 移植：五步流程、可取消版本检查、自动提权、
/// 部署进度本地化与下载速度计算。界面（MainWindow）通过绑定消费这些 ObservableValue。
/// </summary>
internal sealed class InstallerState : IDisposable
{
    private readonly IOnlineInstallService _installService;
    private readonly IPrivacyDeviceIdentityProvider _privacyIdentity;
    private readonly InstallerPrivacyConsentStore _privacyConsentStore;
    private readonly bool _isLocalMode;
    private readonly string? _localZipPath;
    private CancellationTokenSource? _installCts;
    private CancellationTokenSource? _checkCts;

    private long _lastBytesDownloaded;
    private DateTime _lastProgressTime = DateTime.UtcNow;

    public InstallerState(
        IOnlineInstallService installService,
        IPrivacyDeviceIdentityProvider privacyIdentity,
        InstallerPrivacyConsentStore privacyConsentStore,
        string modeLabel,
        bool isLocalMode = false,
        string? localZipPath = null)
    {
        _installService = installService;
        _privacyIdentity = privacyIdentity;
        _privacyConsentStore = privacyConsentStore;
        ModeLabel = modeLabel;
        _isLocalMode = isLocalMode;
        _localZipPath = localZipPath;

        DeviceIdPreview = _privacyIdentity.GetOrCreateDeviceId();
        PrivacyConfirmed.Value = _privacyConsentStore.HasConfirmed(DeviceIdPreview);
        InstallPath.Value = InstallerPathGuard.GetDefaultInstallPath();
        RecalcCanExecute();
    }

    // ---------------- 模式 ----------------

    /// <summary>在线安装 / 本地包安装的模式标签，显示在侧栏副标题。</summary>
    public string ModeLabel { get; }

    // ---------------- 可观察状态 ----------------

    public ObservableValue<InstallerStepId> CurrentStep { get; } = new(InstallerStepId.Welcome);
    public ObservableValue<int> MaxUnlockedStep { get; } = new((int)InstallerStepId.Welcome);
    public ObservableValue<string> InstallPath { get; } = new(string.Empty);
    public ObservableValue<bool> PrivacyConfirmed { get; } = new(false);
    public ObservableValue<string> TargetVersion { get; } = new(string.Empty);
    public ObservableValue<string> SourceId { get; } = new(string.Empty);
    public ObservableValue<string> StatusText { get; } = new("准备开始安装");
    public ObservableValue<double> DownloadProgress { get; } = new(0);
    public ObservableValue<double> InstallProgress { get; } = new(0);
    public ObservableValue<string> CurrentFile { get; } = new(string.Empty);
    public ObservableValue<string> DownloadBytesText { get; } = new(string.Empty);
    public ObservableValue<string> DownloadSpeedText { get; } = new(string.Empty);
    public ObservableValue<bool> IsInstalling { get; } = new(false);
    public ObservableValue<bool> IsCheckingUpdate { get; } = new(false);
    public ObservableValue<bool> IsElevationRequired { get; } = new(false);
    public ObservableValue<string> ElevationMessage { get; } = new("所选安装路径需要管理员权限。");
    public ObservableValue<string> ErrorMessage { get; } = new(string.Empty);
    public ObservableValue<bool> CreateDesktopShortcut { get; } = new(false);
    public ObservableValue<bool> CreateStartupShortcut { get; } = new(false);

    /// <summary>下一步按钮可用性（Deploy/Complete 步为 false，部署用页面内的开始安装）。</summary>
    public ObservableValue<bool> CanGoNext { get; } = new(true);
    public ObservableValue<bool> CanGoBack { get; } = new(false);
    public ObservableValue<bool> CanStartInstall { get; } = new(false);

    public string DeviceIdPreview { get; }

    /// <summary>文件夹选择对话框由窗口层提供（需要 Owner）。</summary>
    public Func<string, Task<string?>>? BrowseRequested { get; set; }

    // ---------------- 步骤推进 ----------------

    public async Task NextAsync()
    {
        ErrorMessage.Value = string.Empty;
        IsElevationRequired.Value = false;

        if (CurrentStep.Value == InstallerStepId.InstallLocation)
        {
            try
            {
                InstallerPathGuard.ValidateInstallPath(InstallPath.Value);
            }
            catch (Exception ex)
            {
                ErrorMessage.Value = ex.Message;
                return;
            }

            if (InstallerElevation.RequiresElevation(InstallPath.Value) && !InstallerElevation.IsRunningElevated())
            {
                IsElevationRequired.Value = true;
                ElevationMessage.Value = $"所选安装路径 {InstallPath.Value} 需要管理员权限才能写入。";
                RecalcCanExecute();
                return;
            }

            _checkCts?.Dispose();
            _checkCts = new CancellationTokenSource();
            IsCheckingUpdate.Value = true;
            RecalcCanExecute();
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutCts.Token, _checkCts.Token);
                var info = await _installService.CheckLatestAsync(linkedCts.Token);
                TargetVersion.Value = info.Version;
                SourceId.Value = info.SourceId;
                StatusText.Value = $"准备安装 {info.Version}";
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                ErrorMessage.Value = "检查更新超时（30秒），请检查网络连接后重试。";
                return;
            }
            catch (OperationCanceledException)
            {
                ErrorMessage.Value = "版本检查已取消。";
                return;
            }
            catch (Exception ex)
            {
                ErrorMessage.Value = ex.Message;
                return;
            }
            finally
            {
                timeoutCts.Dispose();
                IsCheckingUpdate.Value = false;
                RecalcCanExecute();
            }
        }
        else if (CurrentStep.Value == InstallerStepId.PrivacyConfirm)
        {
            _privacyConsentStore.SaveConfirmed(DeviceIdPreview);
        }

        UnlockAndNavigate(CurrentStep.Value + 1);
    }

    public void Back()
    {
        if (IsInstalling.Value || IsCheckingUpdate.Value)
        {
            return;
        }

        if (CurrentStep.Value > InstallerStepId.Welcome)
        {
            CurrentStep.Value = CurrentStep.Value - 1;
        }
    }

    public void SelectStep(InstallerStepId step)
    {
        if (IsInstalling.Value || (int)step > MaxUnlockedStep.Value)
        {
            return;
        }

        CurrentStep.Value = step;
    }

    public void CancelCheck() => _checkCts?.Cancel();

    // ---------------- 提权重启 ----------------

    public void RelaunchElevated()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            var args = $"--install-path \"{InstallPath.Value}\"";
            if (_isLocalMode)
            {
                args += $" --local \"{_localZipPath}\"";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas"
            });

            Environment.Exit(0);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            ErrorMessage.Value = "已拒绝管理员权限请求。请选择一个不需要管理员权限的安装路径，或手动以管理员身份运行安装程序。";
            IsElevationRequired.Value = false;
        }
        catch (Exception ex)
        {
            ErrorMessage.Value = $"重新启动失败：{ex.Message}";
        }
    }

    // ---------------- 部署 ----------------

    public async Task StartInstallAsync()
    {
        ErrorMessage.Value = string.Empty;
        IsInstalling.Value = true;
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        _lastBytesDownloaded = 0;
        _lastProgressTime = DateTime.UtcNow;
        DownloadSpeedText.Value = string.Empty;
        RecalcCanExecute();

        try
        {
            var progress = new Progress<InstallerDeployProgress>(ApplyProgress);
            var options = new OnlineInstallOptions(
                CreateDesktopShortcut.Value,
                CreateStartupShortcut.Value);
            await _installService.InstallFreshAsync(InstallPath.Value, options, progress, _installCts.Token);
            UnlockAndNavigate(InstallerStepId.Complete);
            StatusText.Value = "安装完成";
        }
        catch (OperationCanceledException)
        {
            ErrorMessage.Value = "安装已取消。";
            StatusText.Value = "安装已取消";
        }
        catch (Exception ex)
        {
            ErrorMessage.Value = ex.Message;
            StatusText.Value = "安装失败";
        }
        finally
        {
            IsInstalling.Value = false;
            RecalcCanExecute();
        }
    }

    public void CancelInstall() => _installCts?.Cancel();

    public void Launch()
    {
        var launcher = Path.Combine(InstallPath.Value, OperatingSystem.IsWindows()
            ? "LanMountainDesktop.Launcher.exe"
            : "LanMountainDesktop.Launcher");
        if (!File.Exists(launcher))
        {
            ErrorMessage.Value = "未找到 LanMountainDesktop.Launcher。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = launcher,
                Arguments = "--launch-source postinstall",
                WorkingDirectory = InstallPath.Value,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ErrorMessage.Value = ex.Message;
        }
    }

    /// <summary>窗口层通知安装路径已变更（用于重算提权提示与按钮状态）。</summary>
    public void NotifyInstallPathChanged()
    {
        IsElevationRequired.Value = false;
        RecalcCanExecute();
    }

    // ---------------- 可执行状态重算 ----------------

    private void RecalcCanExecute()
    {
        var step = CurrentStep.Value;
        var busy = IsInstalling.Value || IsCheckingUpdate.Value;

        CanGoBack.Value = step > InstallerStepId.Welcome && !busy;
        CanGoNext.Value = step switch
        {
            InstallerStepId.Welcome => !busy,
            InstallerStepId.InstallLocation => !string.IsNullOrWhiteSpace(InstallPath.Value) && !busy && !IsElevationRequired.Value,
            InstallerStepId.PrivacyConfirm => PrivacyConfirmed.Value && !busy,
            _ => false,
        };
        CanStartInstall.Value = step == InstallerStepId.Deploy
                                && PrivacyConfirmed.Value
                                && !string.IsNullOrWhiteSpace(InstallPath.Value)
                                && !IsInstalling.Value;
    }

    private void UnlockAndNavigate(InstallerStepId step)
    {
        if ((int)step > MaxUnlockedStep.Value)
        {
            MaxUnlockedStep.Value = (int)step;
        }

        CurrentStep.Value = step;
        RecalcCanExecute();
    }

    // ---------------- 进度应用 ----------------

    private void ApplyProgress(InstallerDeployProgress progress)
    {
        StatusText.Value = InstallerStrings.TranslateStage(progress.Stage);
        TargetVersion.Value = progress.TargetVersion ?? TargetVersion.Value;
        DownloadProgress.Value = progress.DownloadProgress;
        InstallProgress.Value = progress.InstallProgress;
        CurrentFile.Value = progress.CurrentFile;
        DownloadBytesText.Value = FormatBytes(progress.BytesDownloaded, progress.TotalBytes);

        var now = DateTime.UtcNow;
        var elapsed = (now - _lastProgressTime).TotalSeconds;
        if (elapsed > 0.5 && progress.BytesDownloaded > _lastBytesDownloaded && progress.BytesDownloaded > 0)
        {
            var speedBytesPerSec = (progress.BytesDownloaded - _lastBytesDownloaded) / elapsed;
            DownloadSpeedText.Value = $"{ToSize((long)speedBytesPerSec)}/s";
            _lastBytesDownloaded = progress.BytesDownloaded;
            _lastProgressTime = now;
        }
    }

    private static string FormatBytes(long downloaded, long? total)
    {
        if (downloaded <= 0 && total is not > 0)
        {
            return string.Empty;
        }

        var downloadedText = ToSize(downloaded);
        return total is > 0 ? $"{downloadedText} / {ToSize(total.Value)}" : downloadedText;
    }

    private static string ToSize(long value)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        var size = (double)value;
        var suffix = 0;
        while (size >= 1024 && suffix < suffixes.Length - 1)
        {
            size /= 1024;
            suffix++;
        }

        return $"{size:0.##} {suffixes[suffix]}";
    }

    public void Dispose()
    {
        CancellationHelper.CancelAndDispose(ref _checkCts);
        CancellationHelper.CancelAndDispose(ref _installCts);
    }
}
