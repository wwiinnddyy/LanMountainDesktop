using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ViewModels;

public sealed partial class PrivacySettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public event Action? ViewPrivacyPolicyRequested;

    public PrivacySettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        RefreshLocalizedText();

        _isInitializing = true;
        Load();
        _isInitializing = false;
    }

    [ObservableProperty]
    private bool _uploadAnonymousCrashData;

    [ObservableProperty]
    private bool _uploadAnonymousUsageData;

    [ObservableProperty]
    private string _telemetryId = string.Empty;

    [ObservableProperty]
    private string _privacyHeader = string.Empty;

    [ObservableProperty]
    private string _crashUploadHeader = string.Empty;

    [ObservableProperty]
    private string _crashUploadDescription = string.Empty;

    [ObservableProperty]
    private string _usageUploadHeader = string.Empty;

    [ObservableProperty]
    private string _usageUploadDescription = string.Empty;

    [ObservableProperty]
    private string _telemetryIdHeader = string.Empty;

    [ObservableProperty]
    private string _telemetryIdDescription = string.Empty;

    [ObservableProperty]
    private string _viewPrivacyPolicyText = string.Empty;

    [ObservableProperty]
    private string _privacyPolicyHintPrefix = string.Empty;

    public void Load()
    {
        var state = _settingsFacade.Privacy.Get();
        UploadAnonymousCrashData = state.UploadAnonymousCrashData;
        UploadAnonymousUsageData = state.UploadAnonymousUsageData;
        TelemetryId = TelemetryServices.Identity?.TelemetryId ?? string.Empty;
    }

    partial void OnUploadAnonymousCrashDataChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        Save();
    }

    partial void OnUploadAnonymousUsageDataChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        Save();
    }

    private void Save()
    {
        _settingsFacade.Privacy.Save(new PrivacySettingsState(
            UploadAnonymousCrashData,
            UploadAnonymousUsageData));
    }

    private void RefreshLocalizedText()
    {
        PrivacyHeader = L("settings.privacy.title", "Privacy");
        CrashUploadHeader = L("settings.privacy.crash_upload_title", "Anonymous crash data uploads");
        CrashUploadDescription = L(
            "settings.privacy.crash_upload_description",
            "Send crash reports to help us improve stability.");
        UsageUploadHeader = L("settings.privacy.usage_upload_title", "Anonymous usage analytics");
        UsageUploadDescription = L(
            "settings.privacy.usage_upload_description",
            "Send usage events to help us understand feature usage and session flow.");
        TelemetryIdHeader = L("settings.privacy.telemetry_id_title", "Telemetry ID");
        TelemetryIdDescription = L(
            "settings.privacy.telemetry_id_description",
            "An anonymous identifier used for detailed telemetry sessions.");
        PrivacyPolicyHintPrefix = L("settings.privacy.policy_hint_prefix", "For more details, please ");
        ViewPrivacyPolicyText = L("settings.privacy.view_policy", "view our privacy policy");
    }

    [RelayCommand]
    private void ViewPrivacyPolicy()
    {
        try
        {
            AppLogger.Info("PrivacySettings", "User requested to view privacy policy.");
            ViewPrivacyPolicyRequested?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PrivacySettings", "Failed to view privacy policy.", ex);
        }
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
