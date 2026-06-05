using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
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
    [NotifyCanExecuteChangedFor(nameof(StartInstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _createDesktopShortcut;

    [ObservableProperty]
    private bool _createStartupShortcut;

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
            new InstallerStepViewModel(InstallerStepId.Welcome, "开始安装", Icon.Play),
            new InstallerStepViewModel(InstallerStepId.InstallLocation, "安装位置", Icon.Folder),
            new InstallerStepViewModel(InstallerStepId.PrivacyConfirm, "数据确认", Icon.Info),
            new InstallerStepViewModel(InstallerStepId.Deploy, "开始部署", Icon.ArrowDownload),
            new InstallerStepViewModel(InstallerStepId.Complete, "完成安装", Icon.Circle)
        ];
        SyncSteps();
        DeviceIdPreview = _privacyIdentity.GetOrCreateDeviceId();
        PrivacyConfirmed = _privacyConsentStore.HasConfirmed(DeviceIdPreview);
    }

    public ObservableCollection<InstallerStepViewModel> Steps { get; }

    public Func<string, Task<string?>>? BrowseRequested { get; set; }

    public string WindowTitle => "LanDesktopPLONDS Installer";

    public string DeviceIdPreview { get; }

    public bool IsWelcomeStep => CurrentStep == InstallerStepId.Welcome;

    public bool IsLocationStep => CurrentStep == InstallerStepId.InstallLocation;

    public bool IsPrivacyStep => CurrentStep == InstallerStepId.PrivacyConfirm;

    public bool IsDeployStep => CurrentStep == InstallerStepId.Deploy;

    public bool IsCompleteStep => CurrentStep == InstallerStepId.Complete;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanGoBack => CurrentStep > InstallerStepId.Welcome && !IsInstalling;

    public bool CanGoNext => CurrentStep switch
    {
        InstallerStepId.Welcome => !IsInstalling,
        InstallerStepId.InstallLocation => !string.IsNullOrWhiteSpace(InstallPath) && !IsInstalling,
        InstallerStepId.PrivacyConfirm => PrivacyConfirmed && !IsInstalling,
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

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        ErrorMessage = null;
        if (CurrentStep == InstallerStepId.InstallLocation)
        {
            try
            {
                InstallerPathGuard.ValidateInstallPath(InstallPath);
                var info = await _installService.CheckLatestAsync(CancellationToken.None);
                TargetVersion = info.Version;
                SourceId = info.SourceId;
                StatusText = $"准备安装 {info.Version}";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                return;
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

    [RelayCommand(CanExecute = nameof(CanStartInstall))]
    private async Task StartInstallAsync()
    {
        ErrorMessage = null;
        IsInstalling = true;
        StartInstallCommand.NotifyCanExecuteChanged();
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
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

    private void ApplyProgress(InstallerDeployProgress progress)
    {
        StatusText = progress.Stage;
        TargetVersion = progress.TargetVersion ?? TargetVersion;
        DownloadProgress = progress.DownloadProgress;
        InstallProgress = progress.InstallProgress;
        CurrentFile = progress.CurrentFile;
        DownloadBytesText = FormatBytes(progress.BytesDownloaded, progress.TotalBytes);
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
}
