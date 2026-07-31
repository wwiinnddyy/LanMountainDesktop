using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanDesktopPLONDS.Installer.Localization;
using LanDesktopPLONDS.Installer.Models;
using LanDesktopPLONDS.Installer.Services;
using LanMountainDesktop.Shared.Contracts.Privacy;

namespace LanDesktopPLONDS.Installer.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IOnlineInstallService _installService;
    private readonly IPrivacyDeviceIdentityProvider _privacyIdentity;
    private readonly InstallerPrivacyConsentStore _privacyConsentStore;
    private CancellationTokenSource? _installCts;
    private CancellationTokenSource? _checkCts;

    // 下载速度计算状态
    private long _lastBytesDownloaded;
    private DateTime _lastProgressTime = DateTime.UtcNow;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartInstallCommand))]
    private InstallerStepId _currentStep = InstallerStepId.Welcome;

    [ObservableProperty]
    private InstallerStepId _maxUnlockedStep = InstallerStepId.Welcome;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartInstallCommand))]
    private string _installPath = InstallerPathGuard.GetDefaultInstallPath();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartInstallCommand))]
    private bool _privacyConfirmed;

    [ObservableProperty]
    private string? _targetVersion;

    [ObservableProperty]
    private string? _sourceId;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _statusText = "准备开始安装";

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string? _currentFile;

    [ObservableProperty]
    private string _downloadBytesText = string.Empty;

    [ObservableProperty]
    private string _downloadSpeedText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartInstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _createDesktopShortcut;

    [ObservableProperty]
    private bool _createStartupShortcut;

    // === Task 1: 可取消的版本检查 ===
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private bool _isCheckingUpdate;

    // === Task 2: 自动提权 ===
    [ObservableProperty]
    private bool _isElevationRequired;

    [ObservableProperty]
    private string _elevationMessage = "所选安装路径需要管理员权限。";

    public MainWindowViewModel(
        IOnlineInstallService installService,
        IPrivacyDeviceIdentityProvider privacyIdentity,
        InstallerPrivacyConsentStore? privacyConsentStore = null)
    {
        _installService = installService;
        _privacyIdentity = privacyIdentity;
        _privacyConsentStore = privacyConsentStore ?? new InstallerPrivacyConsentStore();
        Steps =
        [
            new InstallerStepViewModel(InstallerStepId.Welcome, "开始安装", "\uE768"),
            new InstallerStepViewModel(InstallerStepId.InstallLocation, "安装位置", "\uE838"),
            new InstallerStepViewModel(InstallerStepId.PrivacyConfirm, "数据确认", "\uE946"),
            new InstallerStepViewModel(InstallerStepId.Deploy, "开始部署", "\uE896"),
            new InstallerStepViewModel(InstallerStepId.Complete, "完成安装", "\uE73E")
        ];
        SyncSteps();
        DeviceIdPreview = _privacyIdentity.GetOrCreateDeviceId();
        PrivacyConfirmed = _privacyConsentStore.HasConfirmed(DeviceIdPreview);
    }

    public ObservableCollection<InstallerStepViewModel> Steps { get; }

    public Func<string, Task<string?>>? BrowseRequested { get; set; }

    /// <summary>窗口标题，已本地化。</summary>
    public string WindowTitle => "阑山桌面 安装程序";

    public string DeviceIdPreview { get; }

    public bool IsWelcomeStep => CurrentStep == InstallerStepId.Welcome;

    public bool IsLocationStep => CurrentStep == InstallerStepId.InstallLocation;

    public bool IsPrivacyStep => CurrentStep == InstallerStepId.PrivacyConfirm;

    public bool IsDeployStep => CurrentStep == InstallerStepId.Deploy;

    public bool IsCompleteStep => CurrentStep == InstallerStepId.Complete;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanGoBack => CurrentStep > InstallerStepId.Welcome && !IsInstalling && !IsCheckingUpdate;

    public bool CanGoNext => CurrentStep switch
    {
        InstallerStepId.Welcome => !IsInstalling && !IsCheckingUpdate,
        InstallerStepId.InstallLocation => !string.IsNullOrWhiteSpace(InstallPath) && !IsInstalling && !IsCheckingUpdate,
        InstallerStepId.PrivacyConfirm => PrivacyConfirmed && !IsInstalling && !IsCheckingUpdate,
        _ => false
    };

    public bool CanStartInstall => CurrentStep == InstallerStepId.Deploy &&
                                   PrivacyConfirmed &&
                                   !string.IsNullOrWhiteSpace(InstallPath) &&
                                   !IsInstalling;

    public InstallerWorkflowState Snapshot => new(
        CurrentStep,
        MaxUnlockedStep,
        InstallPath,
        PrivacyConfirmed,
        TargetVersion,
        ErrorMessage);

    partial void OnCurrentStepChanged(InstallerStepId value)
    {
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsLocationStep));
        OnPropertyChanged(nameof(IsPrivacyStep));
        OnPropertyChanged(nameof(IsDeployStep));
        OnPropertyChanged(nameof(IsCompleteStep));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanStartInstall));
        SyncSteps();
    }

    partial void OnErrorMessageChanged(string? value)
    {
        _ = value;
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnMaxUnlockedStepChanged(InstallerStepId value)
    {
        _ = value;
        SyncSteps();
    }

    partial void OnIsInstallingChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanStartInstall));
    }

    partial void OnIsCheckingUpdateChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
    }

    // =====================================================================
    // Task 1: 可取消的版本检查 + Task 2: 自动提权
    // =====================================================================
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        ErrorMessage = null;
        IsElevationRequired = false;

        if (CurrentStep == InstallerStepId.InstallLocation)
        {
            try
            {
                InstallerPathGuard.ValidateInstallPath(InstallPath);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                return;
            }

            // Task 2: 自动提权检查
            if (InstallerElevation.RequiresElevation(InstallPath) && !InstallerElevation.IsRunningElevated())
            {
                IsElevationRequired = true;
                ElevationMessage = $"所选安装路径 {InstallPath} 需要管理员权限才能写入。";
                return;
            }

            // Task 1: 带 CTS 和30秒超时的版本检查
            _checkCts?.Dispose();
            _checkCts = new CancellationTokenSource();
            IsCheckingUpdate = true;
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutCts.Token, _checkCts.Token);
                var info = await _installService.CheckLatestAsync(linkedCts.Token);
                TargetVersion = info.Version;
                SourceId = info.SourceId;
                StatusText = $"准备安装 {info.Version}";
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                ErrorMessage = "检查更新超时（30秒），请检查网络连接后重试。";
                return;
            }
            catch (OperationCanceledException)
            {
                // 用户主动取消
                ErrorMessage = "版本检查已取消。";
                return;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                return;
            }
            finally
            {
                timeoutCts.Dispose();
                IsCheckingUpdate = false;
            }
        }
        else if (CurrentStep == InstallerStepId.PrivacyConfirm)
        {
            _privacyConsentStore.SaveConfirmed(DeviceIdPreview);
        }

        UnlockAndNavigate(CurrentStep + 1);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (IsInstalling)
        {
            return;
        }

        if (CurrentStep > InstallerStepId.Welcome)
        {
            CurrentStep -= 1;
        }
    }

    [RelayCommand]
    private void SelectStep(InstallerStepViewModel? step)
    {
        if (step is null || IsInstalling || step.StepId > MaxUnlockedStep)
        {
            return;
        }

        CurrentStep = step.StepId;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        ErrorMessage = null;
        if (BrowseRequested is null)
        {
            return;
        }

        try
        {
            var selected = await BrowseRequested(InstallPath);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                InstallPath = InstallerPathGuard.GetInstallPathForSelectedFolder(selected);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"选择安装位置失败：{ex.Message}";
        }
    }

    // =====================================================================
    // Task 1: 取消版本检查
    // =====================================================================
    [RelayCommand]
    private void CancelCheck()
    {
        _checkCts?.Cancel();
    }

    // =====================================================================
    // Task 2: 自动提权 — 以管理员身份重新启动
    // =====================================================================
    [RelayCommand]
    private void RelaunchElevated()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            var args = $"--install-path \"{InstallPath}\"";

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
            // 用户在 UAC 对话框中点击了"否"（拒绝提权）
            ErrorMessage = "已拒绝管理员权限请求。请选择一个不需要管理员权限的安装路径，或手动以管理员身份运行安装程序。";
            IsElevationRequired = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"重新启动失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartInstallAsync()
    {
        ErrorMessage = null;
        IsInstalling = true;
        StartInstallCommand.NotifyCanExecuteChanged();
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();

        // 重置下载速度状态
        _lastBytesDownloaded = 0;
        _lastProgressTime = DateTime.UtcNow;
        DownloadSpeedText = string.Empty;

        try
        {
            var progress = new Progress<InstallerDeployProgress>(ApplyProgress);
            var options = new OnlineInstallOptions(CreateDesktopShortcut, CreateStartupShortcut);
            await _installService.InstallFreshAsync(InstallPath, options, progress, _installCts.Token);
            UnlockAndNavigate(InstallerStepId.Complete);
            StatusText = "安装完成";
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "安装已取消。";
            StatusText = "安装已取消";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusText = "安装失败";
        }
        finally
        {
            IsInstalling = false;
            StartInstallCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void CancelInstall()
    {
        _installCts?.Cancel();
    }

    [RelayCommand]
    private void Launch()
    {
        LaunchCore();
    }

    private void LaunchCore()
    {
        var launcher = Path.Combine(InstallPath, OperatingSystem.IsWindows()
            ? "LanMountainDesktop.Launcher.exe"
            : "LanMountainDesktop.Launcher");
        if (!File.Exists(launcher))
        {
            ErrorMessage = "未找到 LanMountainDesktop.Launcher。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = launcher,
                Arguments = "--launch-source postinstall",
                WorkingDirectory = InstallPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void UnlockAndNavigate(InstallerStepId step)
    {
        if (step > MaxUnlockedStep)
        {
            MaxUnlockedStep = step;
        }

        CurrentStep = step;
    }

    // =====================================================================
    // Task 4: 本地化进度阶段 + Task 5: 下载速度显示
    // =====================================================================
    private void ApplyProgress(InstallerDeployProgress progress)
    {
        // Task 4: 将英文阶段键翻译为中文
        StatusText = InstallerStrings.TranslateStage(progress.Stage);
        TargetVersion = progress.TargetVersion ?? TargetVersion;
        DownloadProgress = progress.DownloadProgress;
        InstallProgress = progress.InstallProgress;
        CurrentFile = progress.CurrentFile;
        DownloadBytesText = FormatBytes(progress.BytesDownloaded, progress.TotalBytes);

        // Task 5: 计算下载速度
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastProgressTime).TotalSeconds;
        if (elapsed > 0.5 && progress.BytesDownloaded > _lastBytesDownloaded && progress.BytesDownloaded > 0)
        {
            var speedBytesPerSec = (progress.BytesDownloaded - _lastBytesDownloaded) / elapsed;
            DownloadSpeedText = $"{ToSize((long)speedBytesPerSec)}/s";
            _lastBytesDownloaded = progress.BytesDownloaded;
            _lastProgressTime = now;
        }
    }

    private void SyncSteps()
    {
        foreach (var step in Steps)
        {
            step.IsUnlocked = step.StepId <= MaxUnlockedStep;
            step.IsSelected = step.StepId == CurrentStep;
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

    // =====================================================================
    // Task 2: 解析 --install-path 命令行参数
    // =====================================================================

    /// <summary>
    /// 从命令行参数中解析 --install-path 值。
    /// 由 App.axaml.cs 在启动时调用。
    /// </summary>
    public static string? ParseInstallPath(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return null;
        }

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--install-path", StringComparison.OrdinalIgnoreCase))
            {
                var value = args[i + 1].Trim('"');
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}
