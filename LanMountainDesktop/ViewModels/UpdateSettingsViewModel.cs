using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Shared.Contracts.Update;
using UpdateSettingsValues = LanMountainDesktop.Services.UpdateSettingsValues;

namespace LanMountainDesktop.ViewModels;

public sealed partial class UpdateSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly UpdateOrchestrator _orchestrator;
    private readonly ISettingsFacadeService _settingsFacade;
    private bool _disposed;

    public UpdateSettingsViewModel(UpdateOrchestrator orchestrator, ISettingsFacadeService settingsFacade)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));

        CurrentPhase = _orchestrator.CurrentPhase;
        CurrentVersionText = _settingsFacade.ApplicationInfo.GetAppVersionText();
        LoadPreferenceState();

        _orchestrator.PhaseChanged += OnOrchestratorPhaseChanged;
        _orchestrator.ProgressChanged += OnOrchestratorProgressChanged;
    }

    [ObservableProperty] private UpdatePhase _currentPhase = UpdatePhase.Idle;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _progressDetail = string.Empty;

    [ObservableProperty] private string _currentVersionText = string.Empty;
    [ObservableProperty] private string _latestVersionText = string.Empty;
    [ObservableProperty] private string _publishedAtText = string.Empty;
    [ObservableProperty] private string _lastCheckedText = string.Empty;
    [ObservableProperty] private string _updateTypeText = string.Empty;
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isDeltaUpdate;

    [ObservableProperty] private string _selectedUpdateChannelValue = UpdateSettingsValues.ChannelStable;
    [ObservableProperty] private string _selectedUpdateSourceValue = UpdateSettingsValues.DownloadSourcePdc;
    [ObservableProperty] private string _selectedUpdateModeValue = UpdateSettingsValues.ModeDownloadThenConfirm;
    [ObservableProperty] private double _downloadThreadsSliderValue = UpdateSettingsValues.DefaultDownloadThreads;

    public bool IsBusy => CurrentPhase.IsBusy();
    public bool CanCheck => CurrentPhase.CanCheck();
    public bool CanDownload => CurrentPhase.CanDownload();
    public bool CanInstall => CurrentPhase.CanInstall();
    public bool CanRollback => CurrentPhase.CanRollback();
    public bool IsProgressVisible => CurrentPhase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing or UpdatePhase.Verifying or UpdatePhase.RollingBack;

    partial void OnCurrentPhaseChanged(UpdatePhase value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRollback));
        OnPropertyChanged(nameof(IsProgressVisible));
        CheckCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        RollbackCommand.NotifyCanExecuteChanged();
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

    partial void OnDownloadThreadsSliderValueChanged(double value)
    {
        SavePreferenceState();
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        var report = await _orchestrator.CheckAsync(CancellationToken.None);
        if (report.IsUpdateAvailable)
        {
            IsUpdateAvailable = true;
            LatestVersionText = report.LatestVersion ?? string.Empty;
            PublishedAtText = report.PublishedAt?.ToLocalTime().ToString("g") ?? string.Empty;
            UpdateTypeText = report.PayloadKind?.ToString() ?? string.Empty;
            IsDeltaUpdate = report.PayloadKind is UpdatePayloadKind.DeltaPlonds or UpdatePayloadKind.DeltaLegacy;
            StatusMessage = $"New version {report.LatestVersion} is available.";
        }
        else
        {
            IsUpdateAvailable = false;
            LatestVersionText = string.Empty;
            PublishedAtText = string.Empty;
            UpdateTypeText = string.Empty;
            IsDeltaUpdate = false;
            StatusMessage = report.ErrorMessage ?? "You are up to date.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        StatusMessage = "Downloading update...";
        var result = await _orchestrator.DownloadAsync(CancellationToken.None);
        if (result.Success)
        {
            StatusMessage = "Download complete. Ready to install.";
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? "Download failed.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        StatusMessage = "Installing update...";
        var result = await _orchestrator.InstallAsync(CancellationToken.None);
        if (result.Success)
        {
            StatusMessage = "Update installed successfully.";
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? "Install failed.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRollback))]
    private async Task RollbackAsync()
    {
        StatusMessage = "Rolling back...";
        await _orchestrator.RollbackAsync(CancellationToken.None);
        StatusMessage = "Rollback complete.";
    }

    private void OnOrchestratorPhaseChanged(UpdatePhase phase)
    {
        CurrentPhase = phase;
    }

    private void OnOrchestratorProgressChanged(UpdateProgressReport report)
    {
        ProgressFraction = report.ProgressFraction;
        StatusMessage = report.Message;
        if (report.DownloadDetail is not null)
        {
            ProgressDetail = $"{report.DownloadDetail.CurrentFile} ({report.DownloadDetail.OverallPercent}%)";
        }
        else if (report.InstallDetail is not null)
        {
            ProgressDetail = report.InstallDetail.CurrentFile ?? report.InstallDetail.Message;
        }
        else
        {
            ProgressDetail = string.Empty;
        }
    }

    private void LoadPreferenceState()
    {
        var state = _settingsFacade.Update.Get();
        SelectedUpdateChannelValue = state.UpdateChannel;
        SelectedUpdateSourceValue = state.UpdateDownloadSource;
        SelectedUpdateModeValue = state.UpdateMode;
        DownloadThreadsSliderValue = UpdateSettingsValues.NormalizeDownloadThreads(state.UpdateDownloadThreads);
    }

    private void SavePreferenceState()
    {
        var current = _settingsFacade.Update.Get();
        _settingsFacade.Update.Save(current with
        {
            UpdateChannel = SelectedUpdateChannelValue,
            UpdateDownloadSource = SelectedUpdateSourceValue,
            UpdateMode = SelectedUpdateModeValue,
            UpdateDownloadThreads = UpdateSettingsValues.NormalizeDownloadThreads((int)Math.Round(DownloadThreadsSliderValue))
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _orchestrator.PhaseChanged -= OnOrchestratorPhaseChanged;
        _orchestrator.ProgressChanged -= OnOrchestratorProgressChanged;
    }
}
