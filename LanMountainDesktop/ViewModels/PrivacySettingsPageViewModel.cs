using CommunityToolkit.Mvvm.ComponentModel;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ViewModels;

public sealed partial class PrivacySettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

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
    private string _privacyHeader = string.Empty;

    [ObservableProperty]
    private string _crashUploadHeader = string.Empty;

    [ObservableProperty]
    private string _crashUploadDescription = string.Empty;

    [ObservableProperty]
    private string _usageUploadHeader = string.Empty;

    [ObservableProperty]
    private string _usageUploadDescription = string.Empty;

    public void Load()
    {
        var state = _settingsFacade.Privacy.Get();
        UploadAnonymousCrashData = state.UploadAnonymousCrashData;
        UploadAnonymousUsageData = state.UploadAnonymousUsageData;
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
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
