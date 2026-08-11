using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.ViewModels;

public sealed partial class PrivacyPolicyViewModel : ViewModelBase
{
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _loadingText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _markdownContent = string.Empty;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _hasContent;

    public PrivacyPolicyViewModel()
    {
        _languageCode = "zh-CN";
        RefreshLocalizedText();
        LoadPrivacyPolicy();
    }

    private void RefreshLocalizedText()
    {
        Title = L("settings.privacy.policy_title", "Privacy Policy");
        Description = L("settings.privacy.policy_description", "Learn how we collect, use, and protect your data.");
        LoadingText = L("settings.privacy.policy_loading", "Loading privacy policy...");
    }

    private async void LoadPrivacyPolicy()
    {
        try
        {
            IsLoading = true;
            HasError = false;
            HasContent = false;

            // 从嵌入资源加载隐私政策Markdown文件
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "LanMountainDesktop.Assets.Documents.Privacy.md";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                throw new FileNotFoundException($"Privacy policy resource not found: {resourceName}");
            }

            using var reader = new StreamReader(stream);
            var markdown = await reader.ReadToEndAsync();

            MarkdownContent = markdown;
            IsLoading = false;
            HasContent = true;

            AppLogger.Info("PrivacyPolicy", "Privacy policy loaded successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PrivacyPolicy", "Failed to load privacy policy.", ex);
            IsLoading = false;
            HasError = true;
            ErrorText = L("settings.privacy.policy_error", "Failed to load privacy policy. Please try again later.");
        }
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
