using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.Contracts.Update;
using UpdateSettingsValues = LanMountainDesktop.Services.UpdateSettingsValues;

namespace LanMountainDesktop.ViewModels;

public sealed partial class UpdateSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IUpdateSettingsService _updateSettingsService;
    private readonly LocalizationService _localizationService;
    private readonly string _languageCode;
    private bool _suppressPreferenceSave;
    private bool _disposed;

    public UpdateSettingsViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _updateSettingsService = _settingsFacade.Update;
        _localizationService = new LocalizationService();
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);

        CurrentPhase = _updateSettingsService.CurrentPhase;
        CurrentVersionText = _settingsFacade.ApplicationInfo.GetAppVersionText();
        RefreshLocalizedText();
        LoadPreferenceState();
        StatusMessage = GetPhaseStatusText(CurrentPhase);

        _updateSettingsService.PhaseChanged += OnUpdatePhaseChanged;
        _updateSettingsService.ProgressChanged += OnUpdateProgressChanged;
    }

    [ObservableProperty] private UpdatePhase _currentPhase = UpdatePhase.Idle;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _progressDetail = string.Empty;

    [ObservableProperty] private string _pageTitle = string.Empty;
    [ObservableProperty] private string _pageDescription = string.Empty;
    [ObservableProperty] private string _statusSectionHeader = string.Empty;
    [ObservableProperty] private string _checkCardTitle = string.Empty;
    [ObservableProperty] private string _statusCardTitle = string.Empty;
    [ObservableProperty] private string _statusCardDescription = string.Empty;
    [ObservableProperty] private string _releaseFactsTitle = string.Empty;
    [ObservableProperty] private string _releaseFactsDescription = string.Empty;
    [ObservableProperty] private string _progressTitle = string.Empty;
    [ObservableProperty] private string _progressDescription = string.Empty;
    [ObservableProperty] private string _actionsTitle = string.Empty;
    [ObservableProperty] private string _actionsDescription = string.Empty;
    [ObservableProperty] private string _preferencesTitle = string.Empty;
    [ObservableProperty] private string _preferencesDescription = string.Empty;

    [ObservableProperty] private string _currentVersionLabel = string.Empty;
    [ObservableProperty] private string _latestVersionLabel = string.Empty;
    [ObservableProperty] private string _publishedAtLabel = string.Empty;
    [ObservableProperty] private string _lastCheckedLabel = string.Empty;
    [ObservableProperty] private string _updateTypeLabel = string.Empty;
    [ObservableProperty] private string _smartUpdateLabel = string.Empty;
    [ObservableProperty] private string _smartUpdateDescription = string.Empty;
    [ObservableProperty] private string _smartUpdateOnText = string.Empty;
    [ObservableProperty] private string _smartUpdateOffText = string.Empty;
    [ObservableProperty] private string _modeLabel = string.Empty;
    [ObservableProperty] private string _modeDescription = string.Empty;
    [ObservableProperty] private string _downloadThreadsLabel = string.Empty;
    [ObservableProperty] private string _downloadThreadsDescription = string.Empty;
    [ObservableProperty] private string _forceReinstallLabel = string.Empty;
    [ObservableProperty] private string _forceReinstallDescription = string.Empty;
    [ObservableProperty] private string _resumeSupportLabel = string.Empty;
    [ObservableProperty] private string _resumeSupportDescription = string.Empty;
    [ObservableProperty] private string _transferControlsTitle = string.Empty;
    [ObservableProperty] private string _transferControlsDescription = string.Empty;

    [ObservableProperty] private string _updateAvailableBadgeText = string.Empty;
    [ObservableProperty] private string _pausedBadgeText = string.Empty;
    [ObservableProperty] private string _pausedHintText = string.Empty;
    [ObservableProperty] private string _lastCheckedText = string.Empty;

    [ObservableProperty] private string _checkButtonText = string.Empty;
    [ObservableProperty] private string _downloadButtonText = string.Empty;
    [ObservableProperty] private string _installButtonText = string.Empty;
    [ObservableProperty] private string _pauseButtonText = string.Empty;
    [ObservableProperty] private string _resumeButtonText = string.Empty;
    [ObservableProperty] private string _rollbackButtonText = string.Empty;
    [ObservableProperty] private string _cancelButtonText = string.Empty;

    [ObservableProperty] private string _currentVersionText = string.Empty;
    [ObservableProperty] private string _latestVersionText = string.Empty;
    [ObservableProperty] private string _publishedAtText = string.Empty;
    [ObservableProperty] private string _updateTypeText = string.Empty;
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isDeltaUpdate;
    [ObservableProperty] private bool _forceReinstall;

    [ObservableProperty] private bool _smartUpdateEnabled = true;
    [ObservableProperty] private string _selectedUpdateModeValue = UpdateSettingsValues.ModeSilentDownload;
    [ObservableProperty] private double _downloadThreadsSliderValue = UpdateSettingsValues.DefaultDownloadThreads;

    [ObservableProperty] private SelectionOption? _selectedMode;

    public IReadOnlyList<SelectionOption> ModeOptions { get; private set; } = [];

    public bool IsBusy => CurrentPhase.IsBusy();
    public bool IsPaused => CurrentPhase.IsPaused();
    public bool CanCheck => CurrentPhase.CanCheck();
    public bool CanDownload => IsUpdateAvailable && CurrentPhase.CanDownload();
    public bool CanInstall => CurrentPhase.CanInstall();
    public bool CanRollback => CurrentPhase.CanRollback();
    public bool CanPause => CurrentPhase.CanPause();
    public bool CanResume => CurrentPhase.CanResume();
    public bool CanCancel => CurrentPhase.CanCancel();
    public bool IsProgressVisible => CurrentPhase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.PausedDownloading or UpdatePhase.Installing or UpdatePhase.Verifying or UpdatePhase.RollingBack or UpdatePhase.Recovering;
    public bool IsProgressSectionVisible => IsBusy || IsProgressVisible || IsPaused || HasVisibleAction;
    public string PhaseText => GetPhaseText(CurrentPhase);
    public string LatestVersionDisplayText => string.IsNullOrEmpty(LatestVersionText)
        ? L("settings.update.latest_version_none", "Up to date")
        : LatestVersionText;
    private bool HasVisibleAction => CanDownload || CanInstall || CanRollback || CanPause || CanResume || CanCancel;

    partial void OnCurrentPhaseChanged(UpdatePhase value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRollback));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(IsProgressSectionVisible));
        OnPropertyChanged(nameof(PhaseText));
        CheckCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        RollbackCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(IsProgressSectionVisible));
        DownloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnSmartUpdateEnabledChanged(bool value)
    {
        // Off => manual only; On => keep last non-manual mode or silent download.
        if (!value)
        {
            SelectedUpdateModeValue = UpdateSettingsValues.ModeManual;
        }
        else if (string.Equals(SelectedUpdateModeValue, UpdateSettingsValues.ModeManual, StringComparison.OrdinalIgnoreCase))
        {
            SelectedUpdateModeValue = UpdateSettingsValues.ModeSilentDownload;
        }

        SavePreferenceState();
        SyncComboBoxSelections();
        OnPropertyChanged(nameof(CanCheck));
        CheckCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedUpdateModeValueChanged(string value)
    {
        SmartUpdateEnabled = !string.Equals(value, UpdateSettingsValues.ModeManual, StringComparison.OrdinalIgnoreCase);
        SavePreferenceState();
    }

    partial void OnSelectedModeChanged(SelectionOption? value)
    {
        if (value is not null)
        {
            SelectedUpdateModeValue = value.Value;
        }
    }

    partial void OnDownloadThreadsSliderValueChanged(double value)
    {
        SavePreferenceState();
    }

    partial void OnForceReinstallChanged(bool value)
    {
        SavePreferenceState();
        UpdateTypeText = value
            ? L("settings.update.type_reinstall", "Reinstall")
            : (IsDeltaUpdate
                ? L("settings.update.type_delta", "Incremental Update")
                : UpdateTypeText);
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        StatusMessage = GetCheckingStatusText();
        var report = await _updateSettingsService.CheckAsync(CancellationToken.None);
        LastCheckedText = string.Format(
            CultureInfo.CurrentCulture,
            L("settings.update.last_checked_format", "Last checked: {0}"),
            DateTimeOffset.Now.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

        if (report.IsUpdateAvailable)
        {
            IsUpdateAvailable = true;
            LatestVersionText = report.LatestVersion ?? string.Empty;
            PublishedAtText = report.PublishedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;
            UpdateTypeText = GetUpdateTypeText(report.PayloadKind);
            IsDeltaUpdate = report.PayloadKind is UpdatePayloadKind.DeltaPlonds;
            var availableMessage = report.LatestVersion is null
                ? GetUpdateAvailableStatusText(string.Empty)
                : string.Format(CultureInfo.CurrentCulture, L("settings.update.status_available_format", "New version {0} is available. Download it when you are ready."), report.LatestVersion);
            StatusMessage = string.IsNullOrWhiteSpace(report.ErrorMessage)
                ? availableMessage
                : $"{availableMessage} {report.ErrorMessage}";
        }
        else if (!string.IsNullOrWhiteSpace(report.ErrorMessage))
        {
            IsUpdateAvailable = false;
            LatestVersionText = string.Empty;
            PublishedAtText = string.Empty;
            UpdateTypeText = string.Empty;
            IsDeltaUpdate = false;
            StatusMessage = report.ErrorMessage;
        }
        else
        {
            IsUpdateAvailable = false;
            LatestVersionText = string.Empty;
            PublishedAtText = string.Empty;
            UpdateTypeText = string.Empty;
            IsDeltaUpdate = false;
            StatusMessage = string.IsNullOrWhiteSpace(report.ErrorMessage)
                ? GetUpToDateStatusText()
                : report.ErrorMessage;
        }

        OnPropertyChanged(nameof(LatestVersionDisplayText));
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        StatusMessage = GetDownloadingStatusText();
        var result = await _updateSettingsService.DownloadAsync(CancellationToken.None);
        if (result.Success)
        {
            StatusMessage = GetDownloadCompleteStatusText();
        }
        else if (result.ErrorMessage is not null && result.ErrorMessage.Contains("stale or invalid", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = GetResumeStateInvalidStatusText();
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? GetDownloadFailedStatusText();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        StatusMessage = GetInstallingStatusText();
        var result = await _updateSettingsService.InstallAsync(CancellationToken.None);
        if (result.Success)
        {
            StatusMessage = GetInstallSuccessStatusText();
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? result.ErrorCode ?? GetInstallFailedStatusText();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRollback))]
    private async Task RollbackAsync()
    {
        StatusMessage = GetRollingBackStatusText();
        await _updateSettingsService.RollbackAsync(CancellationToken.None);
        StatusMessage = GetRollbackCompleteStatusText();
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseAsync()
    {
        await _updateSettingsService.PauseAsync();
        StatusMessage = GetPausedStatusText();
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        StatusMessage = GetResumingStatusText();
        var result = await _updateSettingsService.ResumeAsync(CancellationToken.None);
        if (result.Success)
        {
            StatusMessage = GetResumeCompleteStatusText();
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? GetResumeFailedStatusText();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        await _updateSettingsService.CancelAsync();
        StatusMessage = GetCancelStatusText();
        ProgressDetail = string.Empty;
        ProgressFraction = 0;
    }

    private void OnUpdatePhaseChanged(UpdatePhase phase)
    {
        RunOnUiThread(() => CurrentPhase = phase);
    }

    private void OnUpdateProgressChanged(UpdateProgressReport report)
    {
        RunOnUiThread(() => ApplyUpdateProgress(report));
    }

    private void ApplyUpdateProgress(UpdateProgressReport report)
    {
        ProgressFraction = report.ProgressFraction;

        if (report.DownloadDetail is not null)
        {
            StatusMessage = GetDownloadingStatusText();
            ProgressDetail = string.Format(
                CultureInfo.CurrentCulture,
                L("settings.update.progress_download_detail_format", "{0} ({1}%)"),
                report.DownloadDetail.CurrentFile,
                report.DownloadDetail.OverallPercent);
        }
        else if (report.InstallDetail is not null)
        {
            StatusMessage = GetInstallingStatusText();
            ProgressDetail = report.InstallDetail.CurrentFile ?? report.InstallDetail.Message;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(report.Message))
            {
                StatusMessage = report.Message;
            }

            ProgressDetail = string.Empty;
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_disposed)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                action();
            }
        }, DispatcherPriority.Normal);
    }


    private void LoadPreferenceState()
    {
        var state = _updateSettingsService.Get();
        _suppressPreferenceSave = true;
        try
        {
            SelectedUpdateModeValue = state.UpdateMode;
            SmartUpdateEnabled = !string.Equals(state.UpdateMode, UpdateSettingsValues.ModeManual, StringComparison.OrdinalIgnoreCase);
            DownloadThreadsSliderValue = UpdateSettingsValues.NormalizeDownloadThreads(state.UpdateDownloadThreads);
            ForceReinstall = state.ForceUpdateReinstall;
            ApplyPersistedUpdateState(state);
        }
        finally
        {
            _suppressPreferenceSave = false;
        }

        SyncComboBoxSelections();
    }

    private void ApplyPersistedUpdateState(LanMountainDesktop.Services.Settings.UpdateSettingsState state)
    {
        if (!string.IsNullOrWhiteSpace(state.PendingUpdateVersion))
        {
            IsUpdateAvailable = true;
            LatestVersionText = state.PendingUpdateVersion;
            PublishedAtText = state.PendingUpdatePublishedAtUtcMs is > 0
                ? DateTimeOffset
                    .FromUnixTimeMilliseconds(state.PendingUpdatePublishedAtUtcMs.Value)
                    .ToLocalTime()
                    .ToString("g", CultureInfo.CurrentCulture)
                : string.Empty;
            UpdateTypeText = ForceReinstall
                ? L("settings.update.type_reinstall", "Reinstall")
                : UpdateTypeText;
        }

        if (state.LastUpdateCheckUtcMs is > 0)
        {
            LastCheckedText = string.Format(
                CultureInfo.CurrentCulture,
                L("settings.update.last_checked_format", "Last checked: {0}"),
                DateTimeOffset
                    .FromUnixTimeMilliseconds(state.LastUpdateCheckUtcMs.Value)
                    .ToLocalTime()
                    .ToString("g", CultureInfo.CurrentCulture));
        }

        OnPropertyChanged(nameof(LatestVersionDisplayText));
    }

    private void SyncComboBoxSelections()
    {
        SelectedMode = ModeOptions.FirstOrDefault(o => o.Value == SelectedUpdateModeValue)
            ?? ModeOptions.FirstOrDefault();
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.update.title", "更新");
        PageDescription = L("settings.update.description", "通过智慧更新检查并安装正式版。下载源由系统自动使用 S3/CDN，无需手动选择渠道。");
        StatusSectionHeader = L("settings.update.status_section_header", "更新状态");
        CheckCardTitle = L("settings.update.check_card_title", "检查更新");
        StatusCardTitle = L("settings.update.status_card_title", "更新状态");
        StatusCardDescription = L("settings.update.status_card_description", "检查更新，查看版本信息，并在有新版本时继续下载或安装。");
        ReleaseFactsTitle = L("settings.update.release_facts_title", "版本信息");
        ReleaseFactsDescription = L("settings.update.release_facts_description", "显示当前版本、最新发布与更新类型。");
        ProgressTitle = L("settings.update.progress_title", "进度");
        ProgressDescription = L("settings.update.progress_description", "在此查看下载、安装、校验与恢复进度。");
        ActionsTitle = L("settings.update.actions_title", "操作");
        ActionsDescription = L("settings.update.actions_description", "操作按钮会随更新阶段变化，布局保持稳定。");
        PreferencesTitle = L("settings.update.preferences_title", "更新偏好");
        PreferencesDescription = L("settings.update.preferences_description", "开启智慧更新后，系统会自动从官方 CDN 获取增量或全量更新包。");

        CurrentVersionLabel = L("settings.update.current_version_label", "当前版本");
        LatestVersionLabel = L("settings.update.latest_version_label", "最新版本");
        PublishedAtLabel = L("settings.update.published_at_label", "发布时间");
        LastCheckedLabel = L("settings.update.last_checked_label", "上次检查");
        UpdateTypeLabel = L("settings.update.update_type_label", "更新类型");
        SmartUpdateLabel = L("settings.update.smart_update_label", "智慧更新");
        SmartUpdateDescription = L("settings.update.smart_update_description", "开启后自动检查更新：优先官方 S3/CDN，失败时回退 GitHub（含全量重装），无需手动选择渠道。");
        SmartUpdateOnText = L("settings.update.smart_update_on", "开");
        SmartUpdateOffText = L("settings.update.smart_update_off", "关");
        ModeLabel = L("settings.update.mode_label", "更新方式");
        ModeDescription = L("settings.update.mode_description", "手动：不自动下载安装。静默下载：后台下载后确认安装。静默安装：后台下载并在退出时应用。");
        DownloadThreadsLabel = L("settings.update.download_threads_label", "下载线程");
        DownloadThreadsDescription = L("settings.update.download_threads_description", "更新下载使用的并行线程数。");
        ForceReinstallLabel = L("settings.update.force_reinstall_label", "强制全量重装");
        ForceReinstallDescription = L("settings.update.force_reinstall_description", "忽略增量包，始终从官方 CDN 下载完整 Files.zip 并重建部署目录。");
        ResumeSupportLabel = L("settings.update.resume_support_label", "断点续传");
        ResumeSupportDescription = L("settings.update.resume_support_description", "下载会保留临时文件与元数据，可在支持的情况下从中断处继续。");
        TransferControlsTitle = L("settings.update.transfer_controls_title", "传输控制");
        TransferControlsDescription = L("settings.update.transfer_controls_description", "暂停、继续或取消当前更新传输。");

        UpdateAvailableBadgeText = L("settings.update.badge_available", "有可用更新");
        PausedBadgeText = L("settings.update.badge_paused", "已暂停");
        PausedHintText = L("settings.update.paused_hint", "已暂停。继续后可从当前进度恢复。");

        CheckButtonText = L("settings.update.check_button_short", "检查");
        DownloadButtonText = L("settings.update.download_button_short", "下载");
        InstallButtonText = L("settings.update.install_button_short", "安装");
        PauseButtonText = L("settings.update.pause_button_short", "暂停");
        ResumeButtonText = L("settings.update.resume_button_short", "继续");
        RollbackButtonText = L("settings.update.rollback_button_short", "回滚");
        CancelButtonText = L("settings.update.cancel_button_short", "取消");

        LastCheckedText = L("settings.update.last_checked_none", "尚未检查。");

        ModeOptions = CreateModeOptions();
        OnPropertyChanged(nameof(ModeOptions));

        SyncComboBoxSelections();

        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(LatestVersionDisplayText));
    }

    private IReadOnlyList<SelectionOption> CreateModeOptions()
    {
        return
        [
            new(UpdateSettingsValues.ModeSilentDownload, L("settings.update.mode_silent_download", "静默下载")),
            new(UpdateSettingsValues.ModeSilentInstall, L("settings.update.mode_silent_install", "静默安装"))
        ];
    }

    private void SavePreferenceState()
    {
        if (_suppressPreferenceSave)
        {
            return;
        }

        var mode = SmartUpdateEnabled
            ? UpdateSettingsValues.NormalizeMode(SelectedUpdateModeValue)
            : UpdateSettingsValues.ModeManual;
        if (SmartUpdateEnabled && string.Equals(mode, UpdateSettingsValues.ModeManual, StringComparison.OrdinalIgnoreCase))
        {
            mode = UpdateSettingsValues.ModeSilentDownload;
        }

        var current = _updateSettingsService.Get();
        _updateSettingsService.Save(current with
        {
            UpdateChannel = UpdateSettingsValues.ChannelStable,
            UpdateDownloadSource = UpdateSettingsValues.DownloadSourcePlonds,
            UpdateMode = mode,
            UpdateDownloadThreads = UpdateSettingsValues.NormalizeDownloadThreads((int)Math.Round(DownloadThreadsSliderValue)),
            ForceUpdateReinstall = ForceReinstall,
            IncludePrereleaseUpdates = false
            // UseGhProxyMirror: keep existing value; only used when falling back to GitHub full installer.
        });
    }

    private string GetPhaseText(UpdatePhase phase)
    {
        return phase switch
        {
            UpdatePhase.Idle => L("settings.update.phase_idle", "Ready"),
            UpdatePhase.Checking => L("settings.update.phase_checking", "Checking"),
            UpdatePhase.Checked => L("settings.update.phase_checked", "Checked"),
            UpdatePhase.Downloading => L("settings.update.phase_downloading", "Downloading"),
            UpdatePhase.PausedDownloading => L("settings.update.phase_paused_download", "Paused (Download)"),
            UpdatePhase.Downloaded => L("settings.update.phase_downloaded", "Downloaded"),
            UpdatePhase.Installing => L("settings.update.phase_installing", "Installing"),
            UpdatePhase.PausedInstalling => L("settings.update.phase_paused_install", "Paused (Install)"),
            UpdatePhase.Installed => L("settings.update.phase_installed", "Installed"),
            UpdatePhase.Verifying => L("settings.update.phase_verifying", "Verifying"),
            UpdatePhase.Completed => L("settings.update.phase_completed", "Completed"),
            UpdatePhase.Failed => L("settings.update.phase_failed", "Failed"),
            UpdatePhase.Recovering => L("settings.update.phase_recovering", "Recovering"),
            UpdatePhase.RollingBack => L("settings.update.phase_rolling_back", "Rolling Back"),
            UpdatePhase.RolledBack => L("settings.update.phase_rolled_back", "Rolled Back"),
            _ => phase.ToString()
        };
    }


    private string GetPhaseStatusText(UpdatePhase phase)
    {
        return phase switch
        {
            UpdatePhase.Checking => GetCheckingStatusText(),
            UpdatePhase.Downloading => GetDownloadingStatusText(),
            UpdatePhase.PausedDownloading or UpdatePhase.PausedInstalling => GetPausedStatusText(),
            UpdatePhase.Installing => GetInstallingStatusText(),
            UpdatePhase.Recovering => GetRecoveringStatusText(),
            UpdatePhase.RollingBack => GetRollingBackStatusText(),
            UpdatePhase.Completed => GetInstallSuccessStatusText(),
            UpdatePhase.Installed => GetInstallSuccessStatusText(),
            UpdatePhase.RolledBack => GetRollbackCompleteStatusText(),
            UpdatePhase.Failed => L("settings.update.status_failed", "The update failed."),
            _ => GetReadyStatusText()
        };
    }

    private string GetReadyStatusText()
        => L("settings.update.status_ready", "Ready to check for updates.");

    private string GetCheckingStatusText()
        => L("settings.update.status_checking", "Checking update sources...");

    private string GetUpToDateStatusText()
        => L("settings.update.status_up_to_date", "You are already on the latest version.");

    private string GetUpdateAvailableStatusText(string version)
        => string.Format(CultureInfo.CurrentCulture, L("settings.update.status_available_format", "New version {0} is available. Download it when you are ready."), version);

    private string GetDownloadingStatusText()
        => L("settings.update.status_downloading", "Downloading installer...");

    private string GetDownloadCompleteStatusText()
        => L("settings.update.status_launching_installer", "Download complete. You can install the update now.");

    private string GetDownloadFailedStatusText()
        => L("settings.update.status_download_failed", "Download failed.");

    private string GetResumeStateInvalidStatusText()
        => L("settings.update.status_resume_state_invalid", "The resume state is invalid. Cancel and redownload, then try again.");

    private string GetInstallingStatusText()
        => L("settings.update.status_installing", "Installing update...");

    private string GetInstallSuccessStatusText()
        => L("settings.update.status_installed", "Update installed successfully.");

    private string GetInstallFailedStatusText()
        => L("settings.update.status_install_failed", "Install failed.");

    private string GetRollingBackStatusText()
        => L("settings.update.status_rolling_back", "Rolling back...");

    private string GetRollbackCompleteStatusText()
        => L("settings.update.status_rolled_back", "Rollback complete.");

    private string GetPausedStatusText()
        => L("settings.update.status_paused", "Update paused.");

    private string GetResumingStatusText()
        => L("settings.update.status_resuming", "Resuming update...");

    private string GetResumeCompleteStatusText()
        => L("settings.update.status_resumed", "Resume complete.");

    private string GetResumeFailedStatusText()
        => L("settings.update.status_resume_failed", "Resume failed.");

    private string GetRecoveringStatusText()
        => L("settings.update.status_recovering", "Recovering installation...");

    private string GetCancelStatusText()
        => L("settings.update.status_canceled", "Update canceled.");

    private string GetUpdateTypeText(UpdatePayloadKind? payloadKind)
    {
        if (ForceReinstall)
        {
            return L("settings.update.type_reinstall", "Reinstall");
        }

        return payloadKind switch
        {
            UpdatePayloadKind.DeltaPlonds => L("settings.update.type_delta", "Incremental Update"),
            UpdatePayloadKind.FullInstaller => L("settings.update.type_reinstall", "Reinstall"),
            _ => string.Empty
        };
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _updateSettingsService.PhaseChanged -= OnUpdatePhaseChanged;
        _updateSettingsService.ProgressChanged -= OnUpdateProgressChanged;
    }
}
