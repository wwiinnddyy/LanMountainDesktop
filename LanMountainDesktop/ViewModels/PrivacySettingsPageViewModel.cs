using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.PluginSdk;

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
        _settingsFacade = settingsFacade;
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
    private string _deviceId = string.Empty;

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
    private string _deviceIdHeader = string.Empty;

    [ObservableProperty]
    private string _deviceIdDescription = string.Empty;

    [ObservableProperty]
    private string _refreshDeviceIdText = string.Empty;

    [ObservableProperty]
    private string _viewPrivacyPolicyText = string.Empty;

    [ObservableProperty]
    private string _privacyPolicyHintPrefix = string.Empty;

    public void Load()
    {
        var state = _settingsFacade.Privacy.Get();
        UploadAnonymousCrashData = state.UploadAnonymousCrashData;
        UploadAnonymousUsageData = state.UploadAnonymousUsageData;
        DeviceId = DeviceIdService.Instance.DeviceId;
    }

    [RelayCommand]
    private void RefreshDeviceId()
    {
        try
        {
            var deviceInfo = $"{Environment.MachineName}|{Environment.ProcessorCount}|{Environment.OSVersion}|{Environment.UserName}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(deviceInfo));
            var newDeviceId = Convert.ToHexString(hash)[..32].ToLower();

            var snapshot = _settingsFacade.Settings.LoadSnapshot<Models.AppSettingsSnapshot>(SettingsScope.App);
            snapshot.DeviceId = newDeviceId;
            _settingsFacade.Settings.SaveSnapshot(
                SettingsScope.App,
                snapshot,
                changedKeys: [nameof(Models.AppSettingsSnapshot.DeviceId)]);

            DeviceId = newDeviceId;
            AppLogger.Info("PrivacySettings", $"Device ID refreshed: {newDeviceId}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PrivacySettings", "Failed to refresh device ID.", ex);
        }
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
        CrashUploadDescription = L("settings.privacy.crash_upload_description", "Help us improve application stability.");
        UsageUploadHeader = L("settings.privacy.usage_upload_title", "Anonymous usage data uploads");
        UsageUploadDescription = L("settings.privacy.usage_upload_description", "Help us improve application features.");
        DeviceIdHeader = L("settings.privacy.device_id_title", "Device ID");
        DeviceIdDescription = L("settings.privacy.device_id_description", "Unique identifier for this device. Click refresh to regenerate.");
        RefreshDeviceIdText = L("settings.privacy.refresh_device_id", "Refresh");
        PrivacyPolicyHintPrefix = L("settings.privacy.policy_hint_prefix", "For more details, please ");
        ViewPrivacyPolicyText = L("settings.privacy.view_policy", "view our privacy policy");
    }

    [RelayCommand]
    private void ViewPrivacyPolicy()
    {
        try
        {
            // 触发隐私政策查看事件
            AppLogger.Info("PrivacySettings", "User requested to view privacy policy.");
            
            // 发送事件通知显示隐私政策
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
