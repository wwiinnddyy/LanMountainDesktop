using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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
    [ObservableProperty] private string _channelLabel = string.Empty;
    [ObservableProperty] private string _channelDescription = string.Empty;
    [ObservableProperty] private string _sourceLabel = string.Empty;
    [ObservableProperty] private string _sourceDescription = string.Empty;
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

    [ObservableProperty] private string _selectedUpdateChannelValue = UpdateSettingsValues.ChannelStable;
    [ObservableProperty] private string _selectedUpdateSourceValue = UpdateSettingsValues.DownloadSourcePdc;
    [ObservableProperty] private string _selectedUpdateModeValue = UpdateSettingsValues.ModeSilentDownload;
    [ObservableProperty] private double _downloadThreadsSliderValue = UpdateSettingsValues.DefaultDownloadThreads;

    [ObservableProperty] private SelectionOption? _selectedChannel;
    [ObservableProperty] private SelectionOption? _selectedSource;
    [ObservableProperty] private SelectionOption? _selectedMode;

    public IReadOnlyList<SelectionOption> ChannelOptions { get; private set; } = [];
    public IReadOnlyList<SelectionOption> SourceOptions { get; private set; } = [];
    public IReadOnlyList<SelectionOption> ModeOptions { get; private set; } = [];

    public bool IsBusy => CurrentPhase.IsBusy();
    public bool IsPaused => CurrentPhase.IsPaused();
    public bool CanCheck => CurrentPhase.CanCheck();
    public bool CanDownload => CurrentPhase.CanDownload();
    public bool CanInstall => CurrentPhase.CanInstall();
    public bool CanRollback => CurrentPhase.CanRollback();
    public bool CanPause => CurrentPhase.CanPause();
    public bool CanResume => CurrentPhase.CanResume();
    public bool CanCancel => CurrentPhase.CanCancel();
    public bool IsProgressVisible => CurrentPhase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.PausedDownloading or UpdatePhase.Installing or UpdatePhase.Verifying or UpdatePhase.RollingBack or UpdatePhase.Recovering;
    public bool IsProgressSectionVisible => IsBusy || IsProgressVisible || IsPaused;
    public string PhaseText => GetPhaseText(CurrentPhase);
    public string LatestVersionDisplayText => string.IsNullOrEmpty(LatestVersionText)
        ? L("settings.update.latest_version_none", "Up to date")
        : LatestVersionText;

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

    partial void OnSelectedUpdateChannelValueChanged(string value)
    {
        SavePreferenceState();
    }

    partial void OnSelectedUpdateSourceValueChanged(string value)
    {
        SavePreferenceState();
    }

    partial void OnSelectedUpdateModeValueChanged(string value)
    {
        SavePreferenceState();
    }

    partial void OnSelectedChannelChanged(SelectionOption? value)
    {
        if (value is not null)
        {
            SelectedUpdateChannelValue = value.Value;
        }
    }

    partial void OnSelectedSourceChanged(SelectionOption? value)
    {
        if (value is not null)
        {
            SelectedUpdateSourceValue = value.Value;
        }
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
            IsDeltaUpdate = report.PayloadKind is UpdatePayloadKind.DeltaPlonds or UpdatePayloadKind.DeltaLegacy;
            StatusMessage = report.LatestVersion is null
                ? GetUpdateAvailableStatusText(string.Empty)
                : string.Format(CultureInfo.CurrentCulture, L("settings.update.status_available_format", "New version {0} is available. Click Download and Install."), report.LatestVersion);
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
        CurrentPhase = phase;
    }

    private void OnUpdateProgressChanged(UpdateProgressReport report)
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
            StatusMessage = string.IsNullOrWhiteSpace(report.Message)
                ? GetPhaseStatusText(CurrentPhase)
                : report.Message;
            ProgressDetail = string.Empty;
        }
    }


    private void LoadPreferenceState()
    {
        var state = _updateSettingsService.Get();
        _suppressPreferenceSave = true;
        try
        {
            SelectedUpdateChannelValue = state.UpdateChannel;
            SelectedUpdateSourceValue = state.UpdateDownloadSource;
            SelectedUpdateModeValue = state.UpdateMode;
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
        SelectedChannel = ChannelOptions.FirstOrDefault(o => o.Value == SelectedUpdateChannelValue)
            ?? ChannelOptions.FirstOrDefault();
        SelectedSource = SourceOptions.FirstOrDefault(o => o.Value == SelectedUpdateSourceValue)
            ?? SourceOptions.FirstOrDefault();
        SelectedMode = ModeOptions.FirstOrDefault(o => o.Value == SelectedUpdateModeValue)
            ?? ModeOptions.FirstOrDefault();
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.update.title", "Update");
        PageDescription = L("settings.update.description", "Check releases, choose the update channel and download source, and control how updates are installed.");
        StatusSectionHeader = L("settings.update.status_section_header", "Update Status");
        CheckCardTitle = L("settings.update.check_card_title", "Check for Updates");
        StatusCardTitle = L("settings.update.status_card_title", "Update Status");
        StatusCardDescription = L("settings.update.status_card_description", "Check for updates, review release details, and continue with download or installation when a new version is available.");
        ReleaseFactsTitle = L("settings.update.release_facts_title", "Release Facts");
        ReleaseFactsDescription = L("settings.update.release_facts_description", "Keep the current version, published release, and update type visible without collapsing the layout while states change.");
        ProgressTitle = L("settings.update.progress_title", "Progress");
        ProgressDescription = L("settings.update.progress_description", "Watch download, installation, verification, and recovery progress here.");
        ActionsTitle = L("settings.update.actions_title", "Actions");
        ActionsDescription = L("settings.update.actions_description", "The buttons below stay in place while the update phase changes, so the page does not jump around.");
        PreferencesTitle = L("settings.update.preferences_title", "Update Preferences");
        PreferencesDescription = L("settings.update.preferences_description", "Choose the release channel, installer download source, installation behavior, and download parallelism.");

        CurrentVersionLabel = L("settings.update.current_version_label", "Current Version");
        LatestVersionLabel = L("settings.update.latest_version_label", "Latest Release");
        PublishedAtLabel = L("settings.update.published_at_label", "Published At");
        LastCheckedLabel = L("settings.update.last_checked_label", "Last Checked");
        UpdateTypeLabel = L("settings.update.update_type_label", "Update Type");
        ChannelLabel = L("settings.update.channel_label", "Update Channel");
        ChannelDescription = L("settings.update.channel_description", "Choose Stable for regular releases or Preview for earlier builds.");
        SourceLabel = L("settings.update.source_label", "Download Source");
        SourceDescription = L("settings.update.source_description", "Select the manifest and installer source used by the update workflow.");
        ModeLabel = L("settings.update.mode_label", "Update Mode");
        ModeDescription = L("settings.update.mode_description", "Manual never downloads or installs automatically. Silent Download downloads in the background. Silent Install downloads in the background and applies on exit.");
        DownloadThreadsLabel = L("settings.update.download_threads_label", "Download Threads");
        DownloadThreadsDescription = L("settings.update.download_threads_description", "Select how many parallel threads are used for update downloads. Paused downloads can be resumed later.");
        ForceReinstallLabel = L("settings.update.force_reinstall_label", "Force Reinstall");
        ForceReinstallDescription = L("settings.update.force_reinstall_description", "Download the full payload for the selected version and mark this run as a reinstall instead of an incremental update.");
        ResumeSupportLabel = L("settings.update.resume_support_label", "Resume Support");
        ResumeSupportDescription = L("settings.update.resume_support_description", "Downloads keep partial files and package metadata, so Pause and Resume continue from the previous state when the server supports it.");
        TransferControlsTitle = L("settings.update.transfer_controls_title", "Transfer Controls");
        TransferControlsDescription = L("settings.update.transfer_controls_description", "Pause a running download, resume it from the saved state, or cancel and clear pending update artifacts.");

        UpdateAvailableBadgeText = L("settings.update.badge_available", "Update available");
        PausedBadgeText = L("settings.update.badge_paused", "Paused");
        PausedHintText = L("settings.update.paused_hint", "Paused. Resume to continue from the current state.");

        CheckButtonText = L("settings.update.check_button_short", "Check");
        DownloadButtonText = L("settings.update.download_button_short", "Download");
        InstallButtonText = L("settings.update.install_button_short", "Install");
        PauseButtonText = L("settings.update.pause_button_short", "Pause");
        ResumeButtonText = L("settings.update.resume_button_short", "Resume");
        RollbackButtonText = L("settings.update.rollback_button_short", "Rollback");
        CancelButtonText = L("settings.update.cancel_button_short", "Cancel");

        LastCheckedText = L("settings.update.last_checked_none", "Not checked yet.");

        ChannelOptions = CreateChannelOptions();
        SourceOptions = CreateSourceOptions();
        ModeOptions = CreateModeOptions();
        OnPropertyChanged(nameof(ChannelOptions));
        OnPropertyChanged(nameof(SourceOptions));
        OnPropertyChanged(nameof(ModeOptions));

        SyncComboBoxSelections();

        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(LatestVersionDisplayText));
    }

    private IReadOnlyList<SelectionOption> CreateChannelOptions()
    {
        return
        [
            new(UpdateSettingsValues.ChannelStable, L("settings.update.channel_stable", "Stable")),
            new(UpdateSettingsValues.ChannelPreview, L("settings.update.channel_preview", "Preview"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateSourceOptions()
    {
        return
        [
            new(UpdateSettingsValues.DownloadSourcePlonds, L("settings.update.source_plonds", "Plonds CDN")),
            new(UpdateSettingsValues.DownloadSourceGitHub, L("settings.update.source_github", "GitHub")),
            new(UpdateSettingsValues.DownloadSourceGhProxy, L("settings.update.source_gh_proxy", "GitHub Proxy"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateModeOptions()
    {
        return
        [
            new(UpdateSettingsValues.ModeManual, L("settings.update.mode_manual", "Manual: no automatic download or install")),
            new(UpdateSettingsValues.ModeSilentDownload, L("settings.update.mode_silent_download", "Silent Download")),
            new(UpdateSettingsValues.ModeSilentInstall, L("settings.update.mode_silent_install", "Silent Install"))
        ];
    }

    private void SavePreferenceState()
    {
        if (_suppressPreferenceSave)
        {
            return;
        }

        var current = _updateSettingsService.Get();
        _updateSettingsService.Save(current with
        {
            UpdateChannel = SelectedUpdateChannelValue,
            UpdateDownloadSource = SelectedUpdateSourceValue,
            UpdateMode = SelectedUpdateModeValue,
            UpdateDownloadThreads = UpdateSettingsValues.NormalizeDownloadThreads((int)Math.Round(DownloadThreadsSliderValue)),
            ForceUpdateReinstall = ForceReinstall
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
        => L("settings.update.status_checking", "Checking GitHub releases...");

    private string GetUpToDateStatusText()
        => L("settings.update.status_up_to_date", "You are already on the latest version.");

    private string GetUpdateAvailableStatusText(string version)
        => string.Format(CultureInfo.CurrentCulture, L("settings.update.status_available_format", "New version {0} is available. Click Download and Install."), version);

    private string GetDownloadingStatusText()
        => L("settings.update.status_downloading", "Downloading installer...");

    private string GetDownloadCompleteStatusText()
        => L("settings.update.status_launching_installer", "Download complete. Launching installer...");

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
            UpdatePayloadKind.DeltaPlonds or UpdatePayloadKind.DeltaLegacy => L("settings.update.type_delta", "Incremental Update"),
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
