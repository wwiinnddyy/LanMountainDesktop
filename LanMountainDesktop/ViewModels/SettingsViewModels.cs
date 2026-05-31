using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.ViewModels;

public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    private readonly LocalizationService _localizationService;
    private string _languageCode;
    private string _defaultRestartMessage = string.Empty;

    public SettingsWindowViewModel()
    {
        _localizationService = new();
        _languageCode = "zh-CN";
        IsWindowsOs = OperatingSystem.IsWindows();
    }

    public SettingsWindowViewModel(LocalizationService localizationService, string languageCode)
    {
        _localizationService = localizationService;
        _languageCode = languageCode;
        IsWindowsOs = OperatingSystem.IsWindows();
    }

    private string L(string key) => _localizationService.GetString(_languageCode, key, key);

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _currentPageTitle = string.Empty;

    [ObservableProperty]
    private bool _isPageTitleVisible = true;

    [ObservableProperty]
    private string? _currentPageDescription;

    [ObservableProperty]
    private string? _currentPageId;

    [ObservableProperty]
    private bool _isRestartRequested;

    [ObservableProperty]
    private string _restartMessage = string.Empty;

    [ObservableProperty]
    private string _restartTitle = string.Empty;

    [ObservableProperty]
    private string _restartButtonText = string.Empty;

    [ObservableProperty]
    private string _restartDialogTitle = string.Empty;

    [ObservableProperty]
    private string _restartDialogPrimaryText = string.Empty;

    [ObservableProperty]
    private string _restartDialogCloseText = string.Empty;

    [ObservableProperty]
    private string? _drawerTitle;

    [ObservableProperty]
    private string _drawerFallbackTitle = string.Empty;

    [ObservableProperty]
    private bool _isDrawerOpen;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _searchPlaceholderText = string.Empty;

    [ObservableProperty]
    private string _searchNoResultsText = string.Empty;

    [ObservableProperty]
    private string _searchPageHintText = string.Empty;

    [ObservableProperty]
    private SettingsSearchResult? _selectedSearchResult;

    [ObservableProperty]
    private string _moreOptionsText = string.Empty;

    [ObservableProperty]
    private string _restartMenuItemText = string.Empty;

    [ObservableProperty]
    private string _togglePaneTooltip = string.Empty;

    [ObservableProperty]
    private string _backTooltip = string.Empty;

    /// <summary>用于标题栏右侧系统按钮占位（与 SecRandom / ClassIsland 一致，仅 Windows 显示）。</summary>
    [ObservableProperty]
    private bool _isWindowsOs;

    public SettingsWindowViewModel Initialize()
    {
        RefreshLanguage(_languageCode);
        CurrentPageTitle = Title;
        return this;
    }

    public void RefreshLanguage(string? languageCode)
    {
        _languageCode = _localizationService.NormalizeLanguageCode(languageCode);
        Title = L("settings.title");
        RestartTitle = L("settings.restart_dock.title");
        RestartButtonText = L("settings.restart_dock.button");
        RestartDialogTitle = L("settings.restart_dialog.title");
        RestartDialogPrimaryText = L("settings.restart_dialog.restart");
        RestartDialogCloseText = _localizationService.GetString(
            _languageCode,
            "settings.restart_dialog.later",
            L("settings.restart_dialog.cancel"));
        DrawerFallbackTitle = L("settings.window.drawer_default");
        SearchPlaceholderText = L("settings.search.placeholder");
        SearchNoResultsText = L("settings.search.no_results");
        SearchPageHintText = L("settings.search.page_hint");
        MoreOptionsText = L("settings.window.more_options");
        RestartMenuItemText = L("settings.window.restart_menu_item");
        TogglePaneTooltip = L("settings.window.toggle_pane");
        BackTooltip = L("settings.window.back");

        var nextDefaultRestartMessage = L("settings.restart_dock.description");
        if (string.IsNullOrWhiteSpace(RestartMessage) || string.Equals(RestartMessage, _defaultRestartMessage, StringComparison.Ordinal))
        {
            RestartMessage = nextDefaultRestartMessage;
        }

        _defaultRestartMessage = nextDefaultRestartMessage;
    }

    public string GetDefaultRestartMessage() => _defaultRestartMessage;

    public ObservableCollection<SettingsPageDescriptor> Pages { get; } = [];

    public ObservableCollection<SettingsSearchResult> SearchResults { get; } = [];
}

public sealed class SelectionOption
{
    public SelectionOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    public string Value { get; }

    public string Label { get; }
}

public sealed class FluentIconSelectionOption
{
    public FluentIconSelectionOption(string value, string label, Icon icon)
    {
        Value = value;
        Label = label;
        Icon = icon;
    }

    public string Value { get; }

    public string Label { get; }

    public Icon Icon { get; }
}

public sealed class ThemeSeedCandidateOption
{
    public ThemeSeedCandidateOption(string value, string label, Color color, bool isSelected)
    {
        Value = value;
        Label = label;
        Color = color;
        IsSelected = isSelected;
        Brush = new SolidColorBrush(color);
    }

    public string Value { get; }

    public string Label { get; }

    public Color Color { get; }

    public bool IsSelected { get; }

    public IBrush Brush { get; }
}

public sealed class TimeZoneOption
{
    public TimeZoneOption(string? id, string label)
    {
        Id = id;
        Label = label;
    }

    public string? Id { get; }

    public string Label { get; }
}

public sealed partial class GeneralSettingsPageViewModel : ViewModelBase, IDisposable
    {
        private readonly ISettingsFacadeService _settingsFacade;
        private readonly TimeZoneService _timeZoneService;
        private readonly LocalizationService _localizationService = new();
        private readonly string _startupRenderMode;
        private string _languageCode;
        private bool _isInitializing;
        private bool _disposed;

    public GeneralSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _timeZoneService = settingsFacade.Region.GetTimeZoneService();
        _startupRenderMode = Program.StartupRenderMode;

        var regionState = _settingsFacade.Region.Get();
        _languageCode = _localizationService.NormalizeLanguageCode(regionState.LanguageCode);

        Languages = CreateLanguageOptions();
        RenderModes = CreateRenderModeOptions();
        MultiInstanceLaunchBehaviors = CreateMultiInstanceLaunchBehaviorOptions();
        BackToWindowsButtonDisplayModes = CreateBackToWindowsButtonDisplayModeOptions();
        BackToWindowsIconSources = CreateBackToWindowsIconSourceOptions();
        BackToWindowsFluentIcons = CreateBackToWindowsFluentIconOptions();
        FilteredBackToWindowsFluentIcons = BackToWindowsFluentIcons;
        TimeZones = CreateTimeZoneOptions();
        RefreshLocalizedText();

        _isInitializing = true;
        SelectedLanguage = Languages.FirstOrDefault(option =>
            string.Equals(option.Value, regionState.LanguageCode, StringComparison.OrdinalIgnoreCase))
            ?? Languages[0];
        SelectedTimeZone = TimeZones.FirstOrDefault(option =>
            string.Equals(option.Id, regionState.TimeZoneId, StringComparison.OrdinalIgnoreCase))
            ?? TimeZones[0];

        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var normalizedRenderMode = AppRenderingModeHelper.Normalize(appSnapshot.AppRenderMode);
        SelectedRenderMode = RenderModes.FirstOrDefault(option =>
            string.Equals(option.Value, normalizedRenderMode, StringComparison.OrdinalIgnoreCase))
            ?? RenderModes[0];
        SelectedMultiInstanceLaunchBehavior = MultiInstanceLaunchBehaviors.FirstOrDefault(option =>
            string.Equals(option.Value, appSnapshot.MultiInstanceLaunchBehavior.ToString(), StringComparison.OrdinalIgnoreCase))
            ?? MultiInstanceLaunchBehaviors.First(option =>
                string.Equals(option.Value, MultiInstanceLaunchBehavior.NotifyAndOpenDesktop.ToString(), StringComparison.OrdinalIgnoreCase));
        SelectedBackToWindowsButtonDisplayMode = BackToWindowsButtonDisplayModes.FirstOrDefault(option =>
            string.Equals(option.Value, NormalizeBackToWindowsButtonDisplayMode(appSnapshot.BackToWindowsButtonDisplayMode), StringComparison.OrdinalIgnoreCase))
            ?? BackToWindowsButtonDisplayModes[0];
        SelectedBackToWindowsIconSource = BackToWindowsIconSources.FirstOrDefault(option =>
            string.Equals(option.Value, NormalizeBackToWindowsIconSource(appSnapshot.BackToWindowsIconSource), StringComparison.OrdinalIgnoreCase))
            ?? BackToWindowsIconSources[0];
        SelectedBackToWindowsFluentIcon = BackToWindowsFluentIcons.FirstOrDefault(option =>
            string.Equals(option.Value, NormalizeBackToWindowsFluentIconName(appSnapshot.BackToWindowsFluentIconName), StringComparison.OrdinalIgnoreCase))
            ?? BackToWindowsFluentIcons.First(option => string.Equals(option.Value, "Circle", StringComparison.OrdinalIgnoreCase));
        BackToWindowsIconText = NormalizeBackToWindowsIconText(appSnapshot.BackToWindowsIconText);
        ApplyTransitionPreferences(appSnapshot.EnableFadeTransition, appSnapshot.EnableSlideTransition);
        ShowInTaskbar = appSnapshot.ShowInTaskbar;
        _isInitializing = false;

        RefreshPreview();
        
        // 监听设置变更，防止被意外重置
        _settingsFacade.Settings.Changed += OnSettingsChanged;
    }
    
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        _settingsFacade.Settings.Changed -= OnSettingsChanged;
        _disposed = true;
    }
    
    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        if (e.Scope != SettingsScope.App)
        {
            return;
        }
        
        var changedKeys = e.ChangedKeys?.ToArray();
        if (changedKeys is null || changedKeys.Length == 0)
        {
            return;
        }

        if (changedKeys.Contains(nameof(AppSettingsSnapshot.EnableSlideTransition)) ||
            changedKeys.Contains(nameof(AppSettingsSnapshot.EnableFadeTransition)))
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            ApplyTransitionPreferences(snapshot.EnableFadeTransition, snapshot.EnableSlideTransition);
        }

        if (changedKeys.Contains(nameof(AppSettingsSnapshot.ShowInTaskbar)))
        {
            ShowInTaskbar = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App).ShowInTaskbar;
        }

        if (changedKeys.Contains(nameof(AppSettingsSnapshot.MultiInstanceLaunchBehavior)))
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            SelectedMultiInstanceLaunchBehavior = MultiInstanceLaunchBehaviors.FirstOrDefault(option =>
                string.Equals(option.Value, snapshot.MultiInstanceLaunchBehavior.ToString(), StringComparison.OrdinalIgnoreCase))
                ?? MultiInstanceLaunchBehaviors.First(option =>
                    string.Equals(option.Value, MultiInstanceLaunchBehavior.NotifyAndOpenDesktop.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        if (changedKeys.Contains(nameof(AppSettingsSnapshot.BackToWindowsButtonDisplayMode)))
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            SelectedBackToWindowsButtonDisplayMode = BackToWindowsButtonDisplayModes.FirstOrDefault(option =>
                string.Equals(option.Value, NormalizeBackToWindowsButtonDisplayMode(snapshot.BackToWindowsButtonDisplayMode), StringComparison.OrdinalIgnoreCase))
                ?? BackToWindowsButtonDisplayModes[0];
        }

        if (changedKeys.Contains(nameof(AppSettingsSnapshot.BackToWindowsIconSource)) ||
            changedKeys.Contains(nameof(AppSettingsSnapshot.BackToWindowsFluentIconName)) ||
            changedKeys.Contains(nameof(AppSettingsSnapshot.BackToWindowsIconText)))
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            SelectedBackToWindowsIconSource = BackToWindowsIconSources.FirstOrDefault(option =>
                string.Equals(option.Value, NormalizeBackToWindowsIconSource(snapshot.BackToWindowsIconSource), StringComparison.OrdinalIgnoreCase))
                ?? BackToWindowsIconSources[0];
            SelectedBackToWindowsFluentIcon = BackToWindowsFluentIcons.FirstOrDefault(option =>
                string.Equals(option.Value, NormalizeBackToWindowsFluentIconName(snapshot.BackToWindowsFluentIconName), StringComparison.OrdinalIgnoreCase))
                ?? BackToWindowsFluentIcons.First(option => string.Equals(option.Value, "Circle", StringComparison.OrdinalIgnoreCase));
            BackToWindowsIconText = NormalizeBackToWindowsIconText(snapshot.BackToWindowsIconText);
        }
    }

    public event Action? RestartRequested;

    public IReadOnlyList<SelectionOption> Languages { get; }

    public IReadOnlyList<SelectionOption> RenderModes { get; }

    public IReadOnlyList<SelectionOption> MultiInstanceLaunchBehaviors { get; }

    public IReadOnlyList<SelectionOption> BackToWindowsButtonDisplayModes { get; }

    public IReadOnlyList<SelectionOption> BackToWindowsIconSources { get; }

    public IReadOnlyList<FluentIconSelectionOption> BackToWindowsFluentIcons { get; }

    public IReadOnlyList<TimeZoneOption> TimeZones { get; }

    [ObservableProperty]
    private SelectionOption _selectedLanguage = new("zh-CN", "中文");

    [ObservableProperty]
    private TimeZoneOption _selectedTimeZone = new(null, "Follow system default");

    [ObservableProperty]
    private SelectionOption _selectedRenderMode = new(AppRenderingModeHelper.Default, "Default");

    [ObservableProperty]
    private SelectionOption _selectedMultiInstanceLaunchBehavior =
        new(MultiInstanceLaunchBehavior.NotifyAndOpenDesktop.ToString(), "Notify and open desktop");

    [ObservableProperty]
    private SelectionOption _selectedBackToWindowsButtonDisplayMode = new("IconAndText", "Icon and text");

    [ObservableProperty]
    private SelectionOption _selectedBackToWindowsIconSource = new("FluentIcon", "Fluent icon");

    [ObservableProperty]
    private FluentIconSelectionOption _selectedBackToWindowsFluentIcon = new("Circle", "Circle", Icon.Circle);

    [ObservableProperty]
    private IReadOnlyList<FluentIconSelectionOption> _filteredBackToWindowsFluentIcons = [];

    [ObservableProperty]
    private string _backToWindowsFluentIconSearchText = string.Empty;

    [ObservableProperty]
    private string _backToWindowsIconText = "○";

    [ObservableProperty]
    private bool _enableFadeTransition = true;

    [ObservableProperty]
    private bool _enableSlideTransition;

    [ObservableProperty]
    private bool _showInTaskbar;

    [ObservableProperty]
    private string _fadeTransitionHeader = string.Empty;

    [ObservableProperty]
    private string _slideTransitionHeader = string.Empty;

    [ObservableProperty]
    private string _slideTransitionDescription = string.Empty;

    [ObservableProperty]
    private string _showInTaskbarHeader = string.Empty;

    [ObservableProperty]
    private string _showInTaskbarDescription = string.Empty;

    [ObservableProperty]
    private string _multiInstanceLaunchBehaviorHeader = string.Empty;

    [ObservableProperty]
    private string _multiInstanceLaunchBehaviorDescription = string.Empty;

    [ObservableProperty]
    private string _backToWindowsButtonDisplayModeHeader = string.Empty;

    [ObservableProperty]
    private string _backToWindowsButtonDisplayModeDescription = string.Empty;

    [ObservableProperty]
    private string _backToWindowsIconSourceHeader = string.Empty;

    [ObservableProperty]
    private string _backToWindowsIconSourceDescription = string.Empty;

    [ObservableProperty]
    private string _backToWindowsFluentIconHeader = string.Empty;

    [ObservableProperty]
    private string _backToWindowsFluentIconDescription = string.Empty;

    [ObservableProperty]
    private string _backToWindowsFluentIconSearchPlaceholder = string.Empty;

    [ObservableProperty]
    private string _backToWindowsIconTextHeader = string.Empty;

    [ObservableProperty]
    private string _backToWindowsIconTextDescription = string.Empty;

    public bool IsBackToWindowsFluentIconSource =>
        string.Equals(SelectedBackToWindowsIconSource?.Value, "FluentIcon", StringComparison.OrdinalIgnoreCase);

    public bool IsBackToWindowsTextIconSource =>
        string.Equals(SelectedBackToWindowsIconSource?.Value, "Text", StringComparison.OrdinalIgnoreCase);

    public bool IsSlideTransitionAvailable => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

    public bool IsFadeTransitionToggleEnabled => !EnableSlideTransition;

    public string FadeTransitionDescription => EnableSlideTransition
        ? "滑动模式已启用，淡入淡出不可同时使用。"
        : "启用后，启动与恢复过程使用淡入淡出效果。";

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _basicHeader = string.Empty;

    [ObservableProperty]
    private string _runtimeHeader = string.Empty;

    [ObservableProperty]
    private string _runtimeDescription = string.Empty;

    [ObservableProperty]
    private string _languageHeader = string.Empty;

    [ObservableProperty]
    private string _timeZoneHeader = string.Empty;

    [ObservableProperty]
    private string _timeZoneDescription = string.Empty;

    [ObservableProperty]
    private string _renderModeHeader = string.Empty;

    [ObservableProperty]
    private string _previewHeader = string.Empty;

    [ObservableProperty]
    private string _previewTimeLabel = string.Empty;

    [ObservableProperty]
    private string _previewDateLabel = string.Empty;

    [ObservableProperty]
    private string _previewTimeText = string.Empty;

    [ObservableProperty]
    private string _previewDateText = string.Empty;

    [ObservableProperty]
    private string _renderModeRestartMessage = string.Empty;

    partial void OnSelectedLanguageChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        // 更新语言代码并刷新UI文本
        _languageCode = _localizationService.NormalizeLanguageCode(value.Value);
        RefreshLocalizedText();
        RefreshPreview();
        
        // 保存设置
        _settingsFacade.Region.Save(new RegionSettingsState(
            value.Value,
            NormalizeTimeZoneId(SelectedTimeZone?.Id)));
    }

    partial void OnSelectedTimeZoneChanged(TimeZoneOption value)
    {
        RefreshPreview();
        if (_isInitializing || value is null)
        {
            return;
        }

        _settingsFacade.Region.Save(new RegionSettingsState(
            SelectedLanguage.Value,
            NormalizeTimeZoneId(value.Id)));
    }

    partial void OnSelectedRenderModeChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var normalizedRenderMode = AppRenderingModeHelper.Normalize(value.Value);
        appSnapshot.AppRenderMode = normalizedRenderMode;
        _settingsFacade.Settings.SaveSnapshot(
            SettingsScope.App,
            appSnapshot,
            changedKeys: [nameof(AppSettingsSnapshot.AppRenderMode)]);

        var restartRequired = !string.Equals(_startupRenderMode, normalizedRenderMode, StringComparison.OrdinalIgnoreCase);
        PendingRestartStateService.SetPending(PendingRestartStateService.RenderModeReason, restartRequired);
        if (restartRequired)
        {
            RestartRequested?.Invoke();
        }
    }

    partial void OnSelectedMultiInstanceLaunchBehaviorChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        if (!Enum.TryParse<MultiInstanceLaunchBehavior>(value.Value, ignoreCase: true, out var behavior))
        {
            behavior = MultiInstanceLaunchBehavior.NotifyAndOpenDesktop;
        }

        SaveField(nameof(AppSettingsSnapshot.MultiInstanceLaunchBehavior), behavior);
    }

    partial void OnSelectedBackToWindowsButtonDisplayModeChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        SaveField(
            nameof(AppSettingsSnapshot.BackToWindowsButtonDisplayMode),
            NormalizeBackToWindowsButtonDisplayMode(value.Value));
    }

    partial void OnSelectedBackToWindowsIconSourceChanged(SelectionOption value)
    {
        OnPropertyChanged(nameof(IsBackToWindowsFluentIconSource));
        OnPropertyChanged(nameof(IsBackToWindowsTextIconSource));

        if (_isInitializing || value is null)
        {
            return;
        }

        SaveField(
            nameof(AppSettingsSnapshot.BackToWindowsIconSource),
            NormalizeBackToWindowsIconSource(value.Value));
    }

    partial void OnSelectedBackToWindowsFluentIconChanged(FluentIconSelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        SaveField(
            nameof(AppSettingsSnapshot.BackToWindowsFluentIconName),
            NormalizeBackToWindowsFluentIconName(value.Value));
    }

    partial void OnBackToWindowsFluentIconSearchTextChanged(string value)
    {
        var query = value?.Trim() ?? string.Empty;
        FilteredBackToWindowsFluentIcons = string.IsNullOrWhiteSpace(query)
            ? BackToWindowsFluentIcons
            : BackToWindowsFluentIcons
                .Where(option => option.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                 option.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(120)
                .ToList();
    }

    partial void OnBackToWindowsIconTextChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveField(
            nameof(AppSettingsSnapshot.BackToWindowsIconText),
            NormalizeBackToWindowsIconText(value));
    }

    partial void OnEnableSlideTransitionChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveTransitionPreferences(EnableFadeTransition, value);
    }

    partial void OnEnableFadeTransitionChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveTransitionPreferences(value, EnableSlideTransition);
    }

    partial void OnShowInTaskbarChanged(bool value)
    {
        if (_isInitializing) return;
        SaveField(nameof(AppSettingsSnapshot.ShowInTaskbar), value);
    }

    private void SaveField<T>(string key, T value)
    {
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var property = typeof(AppSettingsSnapshot).GetProperty(key);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(snapshot, value);
        }

        _settingsFacade.Settings.SaveSnapshot(SettingsScope.App, snapshot, changedKeys: [key]);
    }

    private void SaveTransitionPreferences(bool enableFadeTransition, bool enableSlideTransition)
    {
        var normalized = StartupVisualPreferencesResolver.FromFlags(enableFadeTransition, enableSlideTransition);
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        snapshot.EnableFadeTransition = normalized.EnableFadeTransition;
        snapshot.EnableSlideTransition = normalized.EnableSlideTransition;
        ApplyTransitionPreferences(normalized.EnableFadeTransition, normalized.EnableSlideTransition);
        _settingsFacade.Settings.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.EnableFadeTransition),
                nameof(AppSettingsSnapshot.EnableSlideTransition)
            ]);
    }

    private void ApplyTransitionPreferences(bool enableFadeTransition, bool enableSlideTransition)
    {
        var normalized = StartupVisualPreferencesResolver.FromFlags(enableFadeTransition, enableSlideTransition);
        var wasInitializing = _isInitializing;
        _isInitializing = true;
        EnableFadeTransition = normalized.EnableFadeTransition;
        EnableSlideTransition = normalized.EnableSlideTransition;
        _isInitializing = wasInitializing;
        OnPropertyChanged(nameof(IsFadeTransitionToggleEnabled));
        OnPropertyChanged(nameof(FadeTransitionDescription));
    }

    private IReadOnlyList<SelectionOption> CreateLanguageOptions()
    {
        return
        [
            new SelectionOption("zh-CN", L("settings.region.language_zh", "中文")),
            new SelectionOption("en-US", L("settings.region.language_en", "English")),
            new SelectionOption("ja-JP", L("settings.region.language_ja", "日本語")),
            new SelectionOption("ko-KR", L("settings.region.language_ko", "한국어"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateRenderModeOptions()
    {
        return
        [
            new SelectionOption(AppRenderingModeHelper.Default, L("settings.about.render_mode.default", "Default")),
            new SelectionOption(AppRenderingModeHelper.Software, L("settings.about.render_mode.software", "Software")),
            new SelectionOption(AppRenderingModeHelper.AngleEgl, L("settings.about.render_mode.angle_egl", "Angle EGL")),
            new SelectionOption(AppRenderingModeHelper.Wgl, L("settings.about.render_mode.wgl", "WGL")),
            new SelectionOption(AppRenderingModeHelper.Vulkan, L("settings.about.render_mode.vulkan", "Vulkan"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateMultiInstanceLaunchBehaviorOptions()
    {
        return
        [
            new SelectionOption(
                MultiInstanceLaunchBehavior.RestartApp.ToString(),
                L("settings.general.multi_instance_behavior.restart", "Restart app")),
            new SelectionOption(
                MultiInstanceLaunchBehavior.OpenDesktopSilently.ToString(),
                L("settings.general.multi_instance_behavior.open_silently", "Open desktop without prompt")),
            new SelectionOption(
                MultiInstanceLaunchBehavior.PromptOnly.ToString(),
                L("settings.general.multi_instance_behavior.prompt_only", "Show prompt only")),
            new SelectionOption(
                MultiInstanceLaunchBehavior.NotifyAndOpenDesktop.ToString(),
                L("settings.general.multi_instance_behavior.notify_and_open", "Notify and open desktop"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateBackToWindowsButtonDisplayModeOptions()
    {
        return
        [
            new SelectionOption(
                "IconAndText",
                L("settings.general.back_to_windows_button_display.icon_and_text", "Icon and text")),
            new SelectionOption(
                "IconOnly",
                L("settings.general.back_to_windows_button_display.icon_only", "Icon only")),
            new SelectionOption(
                "TextOnly",
                L("settings.general.back_to_windows_button_display.text_only", "Text only"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateBackToWindowsIconSourceOptions()
    {
        return
        [
            new SelectionOption(
                "FluentIcon",
                L("settings.general.back_to_windows_icon_source.fluent_icon", "Fluent icon")),
            new SelectionOption(
                "Text",
                L("settings.general.back_to_windows_icon_source.text", "Text icon"))
        ];
    }

    private IReadOnlyList<FluentIconSelectionOption> CreateBackToWindowsFluentIconOptions()
    {
        return Enum.GetValues<Icon>()
            .Select(icon => icon.ToString())
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => new FluentIconSelectionOption(name, name, Enum.Parse<Icon>(name)))
            .ToList();
    }

    private static string NormalizeBackToWindowsButtonDisplayMode(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "IconOnly", StringComparison.OrdinalIgnoreCase) => "IconOnly",
            _ when string.Equals(value, "TextOnly", StringComparison.OrdinalIgnoreCase) => "TextOnly",
            _ => "IconAndText"
        };
    }

    private static string NormalizeBackToWindowsIconSource(string? value)
    {
        return string.Equals(value, "Text", StringComparison.OrdinalIgnoreCase)
            ? "Text"
            : "FluentIcon";
    }

    private static string NormalizeBackToWindowsFluentIconName(string? value)
    {
        return Enum.TryParse<Icon>(value, ignoreCase: true, out var icon)
            ? icon.ToString()
            : Icon.Circle.ToString();
    }

    private static string NormalizeBackToWindowsIconText(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "○"
            : value.Trim();

        var enumerator = StringInfo.GetTextElementEnumerator(normalized);
        var builder = new System.Text.StringBuilder();
        var count = 0;
        while (enumerator.MoveNext() && count < 4)
        {
            builder.Append(enumerator.GetTextElement());
            count++;
        }

        return builder.Length > 0 ? builder.ToString() : "○";
    }

    private IReadOnlyList<TimeZoneOption> CreateTimeZoneOptions()
    {
        return _timeZoneService
            .GetAllTimeZones()
            .Select(zone => new TimeZoneOption(zone.Id, _timeZoneService.GetTimeZoneDisplayName(zone)))
            .Prepend(new TimeZoneOption(null, L("settings.region.follow_system", "Follow system default")))
            .ToList();
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.general.title", "General");
        PageDescription = L("settings.general.description", "Core language, time zone, and runtime behavior.");
        BasicHeader = L("settings.general.basic_header", "Basic Settings");
        RuntimeHeader = L("settings.general.runtime_header", "Runtime");
        RuntimeDescription = L(
            "settings.about.render_mode_desc",
            "Choose the rendering backend. Restart the app after changing this option.");
        LanguageHeader = L("settings.region.language_header", "Language");
        TimeZoneHeader = L("settings.region.timezone_header", "Time Zone");
        TimeZoneDescription = L(
            "settings.region.timezone_desc",
            "Select a time zone. Clock and calendar widgets will follow this zone.");
        RenderModeHeader = L("settings.about.render_mode_header", "App Rendering Mode");
        PreviewHeader = L("settings.general.preview_header", "Date & Time Preview");
        PreviewTimeLabel = L("settings.general.preview_time_label", "Time");
        PreviewDateLabel = L("settings.general.preview_date_label", "Date");
        RenderModeRestartMessage = L(
            "settings.general.render_mode_restart_message",
            "Rendering mode changes require restarting the app.");
        FadeTransitionHeader = L("settings.general.fade_transition_header", "Fade startup transition");
        SlideTransitionHeader = L("settings.general.slide_transition_header", "Slide startup transition");
        SlideTransitionDescription = L(
            "settings.general.slide_transition_desc",
            "Use a slide-in startup transition on supported Windows builds. This option disables fade transition.");
        ShowInTaskbarHeader = L("settings.general.show_main_window_taskbar_header", "Show main desktop window in taskbar");
        ShowInTaskbarDescription = L(
            "settings.general.show_main_window_taskbar_desc",
            "Keep the main desktop host window visible in the taskbar. The independent settings window always has its own taskbar entry.");
        MultiInstanceLaunchBehaviorHeader = L(
            "settings.general.multi_instance_behavior_header",
            "When opening the app again");
        MultiInstanceLaunchBehaviorDescription = L(
            "settings.general.multi_instance_behavior_desc",
            "Choose how Launcher handles repeated launches while LanMountain Desktop is already running.");
        BackToWindowsButtonDisplayModeHeader = L(
            "settings.general.back_to_windows_button_display_header",
            "Back to platform button");
        BackToWindowsButtonDisplayModeDescription = L(
            "settings.general.back_to_windows_button_display_desc",
            "Choose whether the Dock button shows its circle icon, text, or both.");
        BackToWindowsIconSourceHeader = L(
            "settings.general.back_to_windows_icon_source_header",
            "Back button icon source");
        BackToWindowsIconSourceDescription = L(
            "settings.general.back_to_windows_icon_source_desc",
            "Choose whether the left icon slot uses a Fluent icon or short custom text.");
        BackToWindowsFluentIconHeader = L(
            "settings.general.back_to_windows_fluent_icon_header",
            "Fluent icon");
        BackToWindowsFluentIconDescription = L(
            "settings.general.back_to_windows_fluent_icon_desc",
            "Search and choose a built-in Fluent icon for the left icon slot.");
        BackToWindowsFluentIconSearchPlaceholder = L(
            "settings.general.back_to_windows_fluent_icon_search_placeholder",
            "Search icon");
        BackToWindowsIconTextHeader = L(
            "settings.general.back_to_windows_icon_text_header",
            "Text icon");
        BackToWindowsIconTextDescription = L(
            "settings.general.back_to_windows_icon_text_desc",
            "Enter up to four characters to display as the left icon.");
    }

    private void RefreshPreview()
    {
        var culture = ResolveCulture(SelectedLanguage?.Value ?? _languageCode);
        var timeZone = ResolveSelectedTimeZone();
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        PreviewTimeText = now.ToString("T", culture);
        PreviewDateText = now.ToString("D", culture);
    }

    private TimeZoneInfo ResolveSelectedTimeZone()
    {
        var timeZoneId = NormalizeTimeZoneId(SelectedTimeZone?.Id);
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private string? NormalizeTimeZoneId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private CultureInfo ResolveCulture(string? languageCode)
    {
        var normalizedLanguageCode = _localizationService.NormalizeLanguageCode(languageCode);
        try
        {
            return CultureInfo.GetCultureInfo(normalizedLanguageCode);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("zh-CN");
        }
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class AppearanceSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public AppearanceSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        RefreshLocalizedText();
        ThemeModeOptions = CreateThemeModeOptions();

        _isInitializing = true;
        try
        {
            Load();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public event Action<string>? RestartRequested;

    [ObservableProperty]
    private bool _isNightMode;

    [ObservableProperty]
    private IReadOnlyList<SelectionOption> _themeModeOptions = [];

    [ObservableProperty]
    private SelectionOption _selectedThemeMode = new(ThemeAppearanceValues.ThemeModeLight, "Light");

    [ObservableProperty]
    private string _themeModeLabel = string.Empty;

    [ObservableProperty]
    private string _themeModeDescription = string.Empty;

    [ObservableProperty]
    private string _themeModeLightText = string.Empty;

    [ObservableProperty]
    private string _themeModeDarkText = string.Empty;

    [ObservableProperty]
    private string _themeModeFollowSystemText = string.Empty;

    [ObservableProperty]
    private bool _useSystemChrome;

    [ObservableProperty]
    private string _useSystemChromeLabel = string.Empty;

    [ObservableProperty]
    private string _themeHeader = string.Empty;

    [ObservableProperty]
    private string _appearanceRestartMessage = string.Empty;

    public void Load()
    {
        var theme = _settingsFacade.Theme.Get();

        _isInitializing = true;
        try
        {
            ApplySavedState(theme);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    partial void OnSelectedThemeModeChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        var newIsNightMode = value.Value switch
        {
            ThemeAppearanceValues.ThemeModeDark => true,
            ThemeAppearanceValues.ThemeModeLight => false,
            ThemeAppearanceValues.ThemeModeFollowSystem => Application.Current?.ActualThemeVariant == ThemeVariant.Dark,
            _ => IsNightMode
        };

        if (IsNightMode != newIsNightMode)
        {
            IsNightMode = newIsNightMode;
        }

        PersistCurrentState(restartRequired: false);
    }

    partial void OnUseSystemChromeChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        PersistCurrentState(restartRequired: true);
    }

    private void RefreshLocalizedText()
    {
        ThemeHeader = L("settings.appearance.theme_header", "Theme");
        ThemeModeLabel = L("settings.appearance.theme_mode_label", "Theme mode");
        ThemeModeDescription = L("settings.appearance.theme_mode_desc", "Choose light, dark, or follow system preference.");
        ThemeModeLightText = L("settings.appearance.theme_mode.light", "Light");
        ThemeModeDarkText = L("settings.appearance.theme_mode.dark", "Dark");
        ThemeModeFollowSystemText = L("settings.appearance.theme_mode.follow_system", "Follow system");
        UseSystemChromeLabel = L("settings.color.use_system_chrome_toggle", "Use system window chrome");
        AppearanceRestartMessage = L(
            "settings.appearance.restart_message",
            "Window chrome changes require restarting the app.");
    }

    private void ApplySavedState(ThemeAppearanceSettingsState theme)
    {
        IsNightMode = theme.IsNightMode;
        UseSystemChrome = theme.UseSystemChrome;

        var savedThemeMode = NormalizeThemeMode(theme.ThemeMode);
        SelectedThemeMode = ThemeModeOptions.FirstOrDefault(option =>
            string.Equals(option.Value, savedThemeMode, StringComparison.OrdinalIgnoreCase))
            ?? ThemeModeOptions.FirstOrDefault(o => o.Value == ThemeAppearanceValues.ThemeModeLight)
            ?? new SelectionOption(ThemeAppearanceValues.ThemeModeLight, ThemeModeLightText);
    }

    private static string NormalizeThemeMode(string? value)
    {
        if (string.Equals(value, ThemeAppearanceValues.ThemeModeDark, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeAppearanceValues.ThemeModeDark;
        }

        if (string.Equals(value, ThemeAppearanceValues.ThemeModeFollowSystem, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeAppearanceValues.ThemeModeFollowSystem;
        }

        return ThemeAppearanceValues.ThemeModeLight;
    }

    private void PersistCurrentState(bool restartRequired)
    {
        var pendingState = BuildPendingState();
        _settingsFacade.Theme.Save(pendingState);
        var savedState = _settingsFacade.Theme.Get();

        _isInitializing = true;
        try
        {
            ApplySavedState(savedState);
        }
        finally
        {
            _isInitializing = false;
        }

        if (restartRequired)
        {
            RestartRequested?.Invoke(AppearanceRestartMessage);
        }
    }

    private IReadOnlyList<SelectionOption> CreateThemeModeOptions()
    {
        return
        [
            new SelectionOption(ThemeAppearanceValues.ThemeModeLight, ThemeModeLightText),
            new SelectionOption(ThemeAppearanceValues.ThemeModeDark, ThemeModeDarkText),
            new SelectionOption(ThemeAppearanceValues.ThemeModeFollowSystem, ThemeModeFollowSystemText)
        ];
    }

    private ThemeAppearanceSettingsState BuildPendingState()
    {
        return _settingsFacade.Theme.Get() with
        {
            IsNightMode = IsNightMode,
            UseSystemChrome = UseSystemChrome,
            ThemeMode = SelectedThemeMode?.Value ?? ThemeAppearanceValues.ThemeModeLight
        };
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class ComponentsSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public ComponentsSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        SpacingPresets = CreateSpacingPresets();
        RefreshLocalizedText();

        _isInitializing = true;
        Load();
        _isInitializing = false;
    }

    public IReadOnlyList<SelectionOption> SpacingPresets { get; }

    [ObservableProperty]
    private int _shortSideCells;

    [ObservableProperty]
    private double _screenAspectRatio = 16.0 / 9.0;

    [ObservableProperty]
    private int _edgeInsetPercent;

    [ObservableProperty]
    private SelectionOption _selectedSpacingPreset = new("Relaxed", "Relaxed");

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _componentsHeader = string.Empty;

    [ObservableProperty]
    private string _shortSideCellsLabel = string.Empty;

    [ObservableProperty]
    private string _edgeInsetPercentLabel = string.Empty;

    [ObservableProperty]
    private string _spacingPresetLabel = string.Empty;

    [ObservableProperty]
    private string _cornerRadiusStyle = GlobalAppearanceSettings.DefaultCornerRadiusStyle;

    [ObservableProperty]
    private double _cornerRadiusPreviewValue = 24;

    [ObservableProperty]
    private IReadOnlyList<SelectionOption> _cornerRadiusStyleOptions = [];

    [ObservableProperty]
    private SelectionOption? _selectedCornerRadiusStyle;

    [ObservableProperty]
    private string _componentRadiusHeader = string.Empty;

    [ObservableProperty]
    private string _cornerRadiusStyleLabel = string.Empty;

    [ObservableProperty]
    private string _cornerRadiusStyleDescription = string.Empty;

    [ObservableProperty]
    private string _cornerRadiusSpecTooltip = string.Empty;

    public void Load()
    {
        var state = _settingsFacade.Grid.Get();
        ShortSideCells = state.ShortSideCells;
        EdgeInsetPercent = state.EdgeInsetPercent;
        var spacingPreset = _settingsFacade.Grid.NormalizeSpacingPreset(state.SpacingPreset);
        SelectedSpacingPreset = SpacingPresets.FirstOrDefault(option =>
            string.Equals(option.Value, spacingPreset, StringComparison.OrdinalIgnoreCase))
            ?? SpacingPresets[1];

        var theme = _settingsFacade.Theme.Get();
        CornerRadiusStyle = GlobalAppearanceSettings.NormalizeCornerRadiusStyle(theme.CornerRadiusStyle);
        SelectedCornerRadiusStyle = CornerRadiusStyleOptions.FirstOrDefault(option =>
            string.Equals(option.Value, CornerRadiusStyle, StringComparison.OrdinalIgnoreCase))
            ?? CornerRadiusStyleOptions.FirstOrDefault(o => o.Value == GlobalAppearanceSettings.DefaultCornerRadiusStyle);
        CornerRadiusPreviewValue = AppearanceCornerRadiusTokenFactory.Create(CornerRadiusStyle).Component.TopLeft;
    }

    partial void OnShortSideCellsChanged(int value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveGrid();
    }

    partial void OnEdgeInsetPercentChanged(int value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveGrid();
    }

    partial void OnSelectedSpacingPresetChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        SaveGrid();
    }

    partial void OnSelectedCornerRadiusStyleChanged(SelectionOption? value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        CornerRadiusStyle = value.Value;
        CornerRadiusPreviewValue = AppearanceCornerRadiusTokenFactory.Create(value.Value).Component.TopLeft;
        SaveComponentCornerRadius();
    }

    private void SaveGrid()
    {
        _settingsFacade.Grid.Save(new GridSettingsState(
            Math.Clamp(ShortSideCells, 6, 96),
            _settingsFacade.Grid.NormalizeSpacingPreset(SelectedSpacingPreset.Value),
            Math.Clamp(EdgeInsetPercent, 0, 30)));
    }

    private void SaveComponentCornerRadius()
    {
        var theme = _settingsFacade.Theme.Get();
        _settingsFacade.Theme.Save(theme with
        {
            CornerRadiusStyle = GlobalAppearanceSettings.NormalizeCornerRadiusStyle(CornerRadiusStyle)
        });
    }

    private IReadOnlyList<SelectionOption> CreateSpacingPresets()
    {
        return
        [
            new SelectionOption("Compact", L("settings.components.spacing_compact", "Compact")),
            new SelectionOption("Relaxed", L("settings.components.spacing_relaxed", "Relaxed"))
        ];
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.components.title", "Components");
        PageDescription = L("settings.components.description", "Adjust component layout and corner design.");
        ComponentsHeader = L("settings.components.header", "Grid Settings");
        ShortSideCellsLabel = L("settings.components.short_side_label", "Short Side Cells");
        EdgeInsetPercentLabel = L("settings.components.edge_inset_label", "Screen Inset");
        SpacingPresetLabel = L("settings.components.spacing_label", "Component Spacing");
        ComponentRadiusHeader = L("settings.components.corner_radius.header", "Corner Design");
        CornerRadiusStyleLabel = L("settings.components.corner_radius.label", "Component Corner Radius Style");
        CornerRadiusStyleDescription = L(
            "settings.components.corner_radius.description",
            "Select a fixed corner radius style (inspired by Xiaomi HyperOS) to ensure consistency across all components.");
        CornerRadiusSpecTooltip = L("settings.components.corner_radius.spec_tooltip", "View Corner Radius Specification");
            
            
        CornerRadiusStyleOptions = GlobalAppearanceSettings.AllCornerRadiusStyles
            .Select(style => new SelectionOption(style, L($"settings.appearance.corner_radius.style_{style.ToLower()}", style)))
            .ToList();
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class InstalledPluginItemViewModel : ViewModelBase
{
    public InstalledPluginItemViewModel(InstalledPluginInfo info)
    {
        PluginId = info.Manifest.Id;
        Name = info.Manifest.Name;
        Version = info.Manifest.Version ?? "-";
        Description = info.Manifest.Description;
        ErrorMessage = info.ErrorMessage;
        IsLoaded = info.IsLoaded;
        IsPackage = info.IsPackage;
        IsEnabled = info.IsEnabled;
    }

    public string PluginId { get; }

    public string Name { get; }

    public string Version { get; }

    public string? Description { get; }

    public string? ErrorMessage { get; }

    public bool IsLoaded { get; }

    public bool IsPackage { get; }

    [ObservableProperty]
    private bool _isEnabled;
}

public sealed partial class PluginsSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;

    public PluginsSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        RefreshLocalizedText();
        StatusMessage = L(
            "settings.plugins.initial_status",
            "Refresh plugin state to see the latest installed and marketplace entries.");
    }

    public event Action? RestartRequested;

    public ObservableCollection<InstalledPluginItemViewModel> InstalledPlugins { get; } = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _refreshButtonText = string.Empty;

    [ObservableProperty]
    private string _installedHeader = string.Empty;

    [ObservableProperty]
    private string _deleteButtonText = string.Empty;

    [ObservableProperty]
    private string _emptyInstalledText = string.Empty;

    [ObservableProperty]
    private string _restartRequiredMessage = string.Empty;

    public async Task InitializeAsync()
    {
        if (InstalledPlugins.Count > 0)
        {
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            InstalledPlugins.Clear();
            foreach (var plugin in _settingsFacade.PluginManagement.GetInstalledPlugins())
            {
                InstalledPlugins.Add(new InstalledPluginItemViewModel(plugin));
            }

            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L(
                    "settings.plugins.refresh_success_installed_format",
                    "Loaded {0} installed plugins."),
                InstalledPlugins.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void TogglePlugin(InstalledPluginItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (_settingsFacade.PluginManagement.SetPluginEnabled(item.PluginId, item.IsEnabled))
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L(
                    "settings.plugins.toggle_result_format",
                    "Plugin '{0}' was {1} for the next launch. Restart the app to apply page and widget changes."),
                item.Name,
                item.IsEnabled
                    ? L("settings.plugins.toggle_state_enabled", "enabled")
                    : L("settings.plugins.toggle_state_disabled", "disabled"));
            RestartRequested?.Invoke();
        }
        else
        {
            item.IsEnabled = !item.IsEnabled;
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L("settings.plugins.toggle_unchanged_format", "Plugin '{0}' did not change."),
                item.Name);
        }
    }

    [RelayCommand]
    private void DeletePlugin(InstalledPluginItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (_settingsFacade.PluginManagement.DeleteInstalledPlugin(item.PluginId))
        {
            InstalledPlugins.Remove(item);
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L(
                    "settings.plugins.delete_success_format",
                    "Plugin '{0}' was staged for deletion. Restart the app to finish removing it."),
                item.Name);
            RestartRequested?.Invoke();
        }
        else
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L("settings.plugins.delete_failed_name_format", "Failed to remove plugin '{0}'."),
                item.Name);
        }
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.plugins.title", "Plugins");
        PageDescription = L("settings.plugins.description", "Manage installed plugins and review their runtime state.");
        RefreshButtonText = L("settings.plugins.refresh_button", "Refresh Plugins");
        InstalledHeader = L("settings.plugins.installed_header", "Installed Plugins");
        DeleteButtonText = L("settings.plugins.delete_button_short", "Delete");
        EmptyInstalledText = L("settings.plugins.empty", "No plugins found.");
        RestartRequiredMessage = L("settings.plugins.restart_required", "Plugin changes take effect after restart.");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class AboutSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;

    public AboutSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        RefreshLocalizedText();

        VersionText = _settingsFacade.ApplicationInfo.GetAppVersionText();
        CodenameText = _settingsFacade.ApplicationInfo.GetAppCodenameText();
        var backendInfo = _settingsFacade.ApplicationInfo.GetRenderBackendInfo();
        RenderBackendText = string.IsNullOrWhiteSpace(backendInfo.ImplementationTypeName)
            ? backendInfo.ActualBackend
            : $"{backendInfo.ActualBackend} ({backendInfo.ImplementationTypeName})";
    }

    [ObservableProperty]
    private string _versionText = "-";

    [ObservableProperty]
    private string _codenameText = "-";

    [ObservableProperty]
    private string _renderBackendText = "-";

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _appInfoHeader = string.Empty;

    [ObservableProperty]
    private string _versionLabel = string.Empty;

    [ObservableProperty]
    private string _codenameLabel = string.Empty;

    [ObservableProperty]
    private string _renderBackendLabel = string.Empty;

    [ObservableProperty]
    private string _projectResourcesHeader = string.Empty;

    [ObservableProperty]
    private string _linkGitHubText = string.Empty;

    [ObservableProperty]
    private string _linkIssuesText = string.Empty;

    [ObservableProperty]
    private string _copyrightLine = string.Empty;

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.about.title", "About");
        PageDescription = L("settings.about.description", "Application details.");
        AppInfoHeader = L("settings.about.app_info_header", "Application Information");
        VersionLabel = L("settings.about.version_label", "Version");
        CodenameLabel = L("settings.about.codename_label", "Codename");
        RenderBackendLabel = L("settings.about.render_backend_label", "Render Backend");
        ProjectResourcesHeader = L("settings.about.project_resources_header", "Project resources");
        LinkGitHubText = L("settings.about.link_github", "GitHub Repository");
        LinkIssuesText = L("settings.about.link_issues", "Issue Tracker");
        var year = Math.Max(2025, DateTime.UtcNow.Year);
        CopyrightLine = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            L("settings.about.copyright_format", "Copyright (c) 2024-{0} Lincube"),
            year);
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class StudySettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;
    private readonly IStudyAnalyticsService _studyAnalyticsService;

    public StudySettingsPageViewModel(ISettingsFacadeService settingsFacade, IStudyAnalyticsService? studyAnalyticsService = null)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _studyAnalyticsService = studyAnalyticsService ?? StudyAnalyticsServiceFactory.CreateDefault();
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);

        RefreshLocalizedText();

        _isInitializing = true;
        LoadSettings();
        _isInitializing = false;
    }

    #region Properties - Master Switch

    [ObservableProperty]
    private string _masterSwitchHeader = string.Empty;

    [ObservableProperty]
    private string _masterSwitchDescription = string.Empty;

    [ObservableProperty]
    private bool _studyEnabled = true;

    partial void OnStudyEnabledChanged(bool value)
    {
        if (!_isInitializing)
        {
            SaveMasterSwitch();
        }
    }

    private void SaveMasterSwitch()
    {
        try
        {
            var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            appSnapshot.StudyEnabled = StudyEnabled;
            _settingsFacade.Settings.SaveSnapshot(SettingsScope.App, appSnapshot,
                changedKeys: [nameof(AppSettingsSnapshot.StudyEnabled)]);
        }
        catch (Exception)
        {
            // 静默处理错误，避免影响用户体验
        }
    }

    #endregion

    #region Properties - Noise Monitoring

    [ObservableProperty]
    private string _noiseMonitoringHeader = string.Empty;

    [ObservableProperty]
    private string _noiseMonitoringDescription = string.Empty;

    [ObservableProperty]
    private string _samplingRateLabel = string.Empty;

    [ObservableProperty]
    private string _samplingRateDescription = string.Empty;

    [ObservableProperty]
    private int _samplingRateMs = 50;

    [ObservableProperty]
    private string _samplingRateValueText = string.Empty;

    [ObservableProperty]
    private string _noiseSensitivityLabel = string.Empty;

    [ObservableProperty]
    private string _noiseSensitivityDescription = string.Empty;

    [ObservableProperty]
    private double _noiseSensitivityDbfs = -50;

    [ObservableProperty]
    private string _noiseSensitivityValueText = string.Empty;

    [ObservableProperty]
    private string _currentThresholdText = string.Empty;

    partial void OnNoiseSensitivityDbfsChanged(double value)
    {
        // 输入验证：限制在合理范围内
        if (value < -70 || value > -35)
        {
            NoiseSensitivityDbfs = Math.Clamp(value, -70, -35);
            return;
        }

        UpdateSensitivityText();
        UpdateThresholdText();
        if (!_isInitializing)
        {
            SaveNoiseSettings();
        }
    }

    partial void OnSamplingRateMsChanged(int value)
    {
        // 输入验证：限制在合理范围内
        if (value < 20 || value > 200)
        {
            SamplingRateMs = Math.Clamp(value, 20, 200);
            return;
        }

        UpdateSamplingRateText();
        if (!_isInitializing)
        {
            SaveNoiseSettings();
        }
    }

    private void UpdateSamplingRateText()
    {
        SamplingRateValueText = $"{SamplingRateMs}ms";
    }

    private void UpdateSensitivityText()
    {
        NoiseSensitivityValueText = $"{NoiseSensitivityDbfs:F0} dBFS";
    }

    private void SaveNoiseSettings()
    {
        try
        {
            var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            appSnapshot.StudyFrameMs = SamplingRateMs;
            appSnapshot.StudyScoreThresholdDbfs = NoiseSensitivityDbfs;
            _settingsFacade.Settings.SaveSnapshot(SettingsScope.App, appSnapshot,
                changedKeys: [nameof(AppSettingsSnapshot.StudyFrameMs), nameof(AppSettingsSnapshot.StudyScoreThresholdDbfs)]);
            UpdateThresholdText();
            UpdateStudyAnalyticsConfig();
        }
        catch (Exception)
        {
            // 静默处理错误
        }
    }

    #endregion

    #region Properties - Focus Timer

    [ObservableProperty]
    private string _focusTimerHeader = string.Empty;

    [ObservableProperty]
    private string _focusTimerDescription = string.Empty;

    [ObservableProperty]
    private string _focusDurationLabel = string.Empty;

    [ObservableProperty]
    private string _focusDurationDescription = string.Empty;

    [ObservableProperty]
    private int _focusDurationMinutes = 25;

    [ObservableProperty]
    private string _focusDurationValueText = string.Empty;

    [ObservableProperty]
    private string _breakDurationLabel = string.Empty;

    [ObservableProperty]
    private string _breakDurationDescription = string.Empty;

    [ObservableProperty]
    private int _breakDurationMinutes = 5;

    [ObservableProperty]
    private string _breakDurationValueText = string.Empty;

    [ObservableProperty]
    private string _longBreakDurationLabel = string.Empty;

    [ObservableProperty]
    private string _longBreakDurationDescription = string.Empty;

    [ObservableProperty]
    private int _longBreakDurationMinutes = 15;

    [ObservableProperty]
    private string _longBreakDurationValueText = string.Empty;

    [ObservableProperty]
    private string _sessionsBeforeLongBreakLabel = string.Empty;

    [ObservableProperty]
    private string _sessionsBeforeLongBreakDescription = string.Empty;

    [ObservableProperty]
    private int _sessionsBeforeLongBreak = 4;

    [ObservableProperty]
    private string _sessionsBeforeLongBreakValueText = string.Empty;

    [ObservableProperty]
    private string _autoStartBreakLabel = string.Empty;

    [ObservableProperty]
    private string _autoStartBreakDescription = string.Empty;

    [ObservableProperty]
    private bool _autoStartBreak;

    [ObservableProperty]
    private string _autoStartFocusLabel = string.Empty;

    [ObservableProperty]
    private string _autoStartFocusDescription = string.Empty;

    [ObservableProperty]
    private bool _autoStartFocus;

    partial void OnFocusDurationMinutesChanged(int value)
    {
        // 输入验证
        if (value < 5 || value > 90)
        {
            FocusDurationMinutes = Math.Clamp(value, 5, 90);
            return;
        }

        UpdateFocusDurationText();
        if (!_isInitializing)
        {
            SaveTimerSettings();
        }
    }

    partial void OnBreakDurationMinutesChanged(int value)
    {
        // 输入验证
        if (value < 1 || value > 30)
        {
            BreakDurationMinutes = Math.Clamp(value, 1, 30);
            return;
        }

        UpdateBreakDurationText();
        if (!_isInitializing)
        {
            SaveTimerSettings();
        }
    }

    partial void OnLongBreakDurationMinutesChanged(int value)
    {
        // 输入验证
        if (value < 5 || value > 60)
        {
            LongBreakDurationMinutes = Math.Clamp(value, 5, 60);
            return;
        }

        UpdateLongBreakDurationText();
        if (!_isInitializing)
        {
            SaveTimerSettings();
        }
    }

    partial void OnSessionsBeforeLongBreakChanged(int value)
    {
        // 输入验证
        if (value < 2 || value > 8)
        {
            SessionsBeforeLongBreak = Math.Clamp(value, 2, 8);
            return;
        }

        UpdateSessionsBeforeLongBreakText();
        if (!_isInitializing)
        {
            SaveTimerSettings();
        }
    }

    partial void OnAutoStartBreakChanged(bool value)
    {
        if (!_isInitializing)
        {
            SaveTimerSettings();
        }
    }

    partial void OnAutoStartFocusChanged(bool value)
    {
        if (!_isInitializing)
        {
            SaveTimerSettings();
        }
    }

    private void UpdateFocusDurationText()
    {
        var unit = L("common.unit.minutes", "分钟");
        FocusDurationValueText = $"{FocusDurationMinutes} {unit}";
    }

    private void UpdateBreakDurationText()
    {
        var unit = L("common.unit.minutes", "分钟");
        BreakDurationValueText = $"{BreakDurationMinutes} {unit}";
    }

    private void UpdateLongBreakDurationText()
    {
        var unit = L("common.unit.minutes", "分钟");
        LongBreakDurationValueText = $"{LongBreakDurationMinutes} {unit}";
    }

    private void UpdateSessionsBeforeLongBreakText()
    {
        var unit = L("common.unit.times", "次");
        SessionsBeforeLongBreakValueText = $"{SessionsBeforeLongBreak} {unit}";
    }

    private void SaveTimerSettings()
    {
        try
        {
            var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            appSnapshot.StudyFocusDurationMinutes = FocusDurationMinutes;
            appSnapshot.StudyBreakDurationMinutes = BreakDurationMinutes;
            appSnapshot.StudyLongBreakDurationMinutes = LongBreakDurationMinutes;
            appSnapshot.StudySessionsBeforeLongBreak = SessionsBeforeLongBreak;
            appSnapshot.StudyAutoStartBreak = AutoStartBreak;
            appSnapshot.StudyAutoStartFocus = AutoStartFocus;
            _settingsFacade.Settings.SaveSnapshot(SettingsScope.App, appSnapshot,
                changedKeys: [
                    nameof(AppSettingsSnapshot.StudyFocusDurationMinutes),
                    nameof(AppSettingsSnapshot.StudyBreakDurationMinutes),
                    nameof(AppSettingsSnapshot.StudyLongBreakDurationMinutes),
                    nameof(AppSettingsSnapshot.StudySessionsBeforeLongBreak),
                    nameof(AppSettingsSnapshot.StudyAutoStartBreak),
                    nameof(AppSettingsSnapshot.StudyAutoStartFocus)
                ]);
        }
        catch (Exception)
        {
            // 静默处理错误
        }
    }

    #endregion

    #region Properties - Alert

    [ObservableProperty]
    private string _alertHeader = string.Empty;

    [ObservableProperty]
    private string _alertDescription = string.Empty;

    [ObservableProperty]
    private string _noiseAlertEnabledLabel = string.Empty;

    [ObservableProperty]
    private string _noiseAlertEnabledDescription = string.Empty;

    [ObservableProperty]
    private bool _noiseAlertEnabled;

    [ObservableProperty]
    private string _maxInterruptsPerMinuteLabel = string.Empty;

    [ObservableProperty]
    private string _maxInterruptsPerMinuteDescription = string.Empty;

    [ObservableProperty]
    private int _maxInterruptsPerMinute = 6;

    partial void OnNoiseAlertEnabledChanged(bool value)
    {
        if (!_isInitializing)
        {
            SaveAlertSettings();
        }
    }

    partial void OnMaxInterruptsPerMinuteChanged(int value)
    {
        // 输入验证
        if (value < 3 || value > 20)
        {
            MaxInterruptsPerMinute = Math.Clamp(value, 3, 20);
            return;
        }

        if (!_isInitializing)
        {
            SaveAlertSettings();
        }
    }

    private void SaveAlertSettings()
    {
        try
        {
            var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            appSnapshot.StudyNoiseAlertEnabled = NoiseAlertEnabled;
            appSnapshot.StudyMaxInterruptsPerMinute = MaxInterruptsPerMinute;
            _settingsFacade.Settings.SaveSnapshot(SettingsScope.App, appSnapshot,
                changedKeys: [nameof(AppSettingsSnapshot.StudyNoiseAlertEnabled), nameof(AppSettingsSnapshot.StudyMaxInterruptsPerMinute)]);
            UpdateStudyAnalyticsConfig();
        }
        catch (Exception)
        {
            // 静默处理错误
        }
    }

    #endregion

    #region Properties - Display

    [ObservableProperty]
    private string _displayHeader = string.Empty;

    [ObservableProperty]
    private string _displayDescription = string.Empty;

    [ObservableProperty]
    private string _showRealtimeDbLabel = string.Empty;

    [ObservableProperty]
    private string _showRealtimeDbDescription = string.Empty;

    [ObservableProperty]
    private bool _showRealtimeDb = true;

    [ObservableProperty]
    private string _baselineDbLabel = string.Empty;

    [ObservableProperty]
    private string _baselineDbDescription = string.Empty;

    [ObservableProperty]
    private double _baselineDb = 45;

    [ObservableProperty]
    private string _baselineDbValueText = string.Empty;

    [ObservableProperty]
    private string _avgWindowSecLabel = string.Empty;

    [ObservableProperty]
    private string _avgWindowSecDescription = string.Empty;

    [ObservableProperty]
    private int _avgWindowSec = 1;

    [ObservableProperty]
    private string _avgWindowSecValueText = string.Empty;

    partial void OnShowRealtimeDbChanged(bool value)
    {
        if (!_isInitializing)
        {
            SaveDisplaySettings();
        }
    }

    partial void OnBaselineDbChanged(double value)
    {
        // 输入验证
        if (value < 20 || value > 90)
        {
            BaselineDb = Math.Clamp(value, 20, 90);
            return;
        }

        UpdateBaselineDbText();
        if (!_isInitializing)
        {
            SaveDisplaySettings();
        }
    }

    partial void OnAvgWindowSecChanged(int value)
    {
        // 输入验证
        if (value < 1 || value > 8)
        {
            AvgWindowSec = Math.Clamp(value, 1, 8);
            return;
        }

        UpdateAvgWindowSecText();
        if (!_isInitializing)
        {
            SaveDisplaySettings();
        }
    }

    #endregion

    [ObservableProperty]
    private string _footerHint = string.Empty;

    private void UpdateBaselineDbText()
    {
        BaselineDbValueText = $"{BaselineDb:F0} dB";
    }

    private void UpdateAvgWindowSecText()
    {
        var unit = L("common.unit.seconds", "秒");
        AvgWindowSecValueText = $"{AvgWindowSec} {unit}";
    }

    private void SaveDisplaySettings()
    {
        try
        {
            var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            appSnapshot.StudyShowRealtimeDb = ShowRealtimeDb;
            appSnapshot.StudyBaselineDb = BaselineDb;
            appSnapshot.StudyAvgWindowSec = AvgWindowSec;
            _settingsFacade.Settings.SaveSnapshot(SettingsScope.App, appSnapshot,
                changedKeys: [nameof(AppSettingsSnapshot.StudyShowRealtimeDb), nameof(AppSettingsSnapshot.StudyBaselineDb), nameof(AppSettingsSnapshot.StudyAvgWindowSec)]);
            UpdateStudyAnalyticsConfig();
        }
        catch (Exception)
        {
            // 静默处理错误
        }
    }

    private void LoadSettings()
    {
        try
        {
            var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);

            // Master switch - 确保正确加载保存的值
            StudyEnabled = appSnapshot.StudyEnabled;

            // Noise settings
            SamplingRateMs = appSnapshot.StudyFrameMs is > 0 ? appSnapshot.StudyFrameMs.Value : 50;
            NoiseSensitivityDbfs = appSnapshot.StudyScoreThresholdDbfs ?? -50;

            // Timer settings
            FocusDurationMinutes = appSnapshot.StudyFocusDurationMinutes is > 0 ? appSnapshot.StudyFocusDurationMinutes.Value : 25;
            BreakDurationMinutes = appSnapshot.StudyBreakDurationMinutes is > 0 ? appSnapshot.StudyBreakDurationMinutes.Value : 5;
            LongBreakDurationMinutes = appSnapshot.StudyLongBreakDurationMinutes is > 0 ? appSnapshot.StudyLongBreakDurationMinutes.Value : 15;
            SessionsBeforeLongBreak = appSnapshot.StudySessionsBeforeLongBreak is > 0 ? appSnapshot.StudySessionsBeforeLongBreak.Value : 4;
            AutoStartBreak = appSnapshot.StudyAutoStartBreak ?? false;
            AutoStartFocus = appSnapshot.StudyAutoStartFocus ?? false;

            // Alert settings
            NoiseAlertEnabled = appSnapshot.StudyNoiseAlertEnabled ?? false;
            MaxInterruptsPerMinute = appSnapshot.StudyMaxInterruptsPerMinute is > 0 ? appSnapshot.StudyMaxInterruptsPerMinute.Value : 6;

            // Display settings
            ShowRealtimeDb = appSnapshot.StudyShowRealtimeDb ?? true;
            BaselineDb = appSnapshot.StudyBaselineDb ?? 45;
            AvgWindowSec = appSnapshot.StudyAvgWindowSec ?? 1;

            UpdateSamplingRateText();
            UpdateSensitivityText();
            UpdateThresholdText();
            UpdateFocusDurationText();
            UpdateBreakDurationText();
            UpdateLongBreakDurationText();
            UpdateSessionsBeforeLongBreakText();
            UpdateBaselineDbText();
            UpdateAvgWindowSecText();
        }
        catch (Exception)
        {
            // 加载失败时使用默认值
            StudyEnabled = true;
            SamplingRateMs = 50;
            NoiseSensitivityDbfs = -50;
            FocusDurationMinutes = 25;
            BreakDurationMinutes = 5;
            LongBreakDurationMinutes = 15;
            SessionsBeforeLongBreak = 4;
            AutoStartBreak = false;
            AutoStartFocus = false;
            NoiseAlertEnabled = false;
            MaxInterruptsPerMinute = 6;
            ShowRealtimeDb = true;
            BaselineDb = 45;
            AvgWindowSec = 1;
        }
    }

    private void UpdateStudyAnalyticsConfig()
    {
        var currentConfig = _studyAnalyticsService.GetConfig();
        var newConfig = currentConfig with
        {
            FrameMs = SamplingRateMs,
            ScoreThresholdDbfs = NoiseSensitivityDbfs,
            BaselineDb = BaselineDb,
            AvgWindowSec = AvgWindowSec,
            ShowRelativeDb = ShowRealtimeDb,
            MaxSegmentsPerMin = MaxInterruptsPerMinute,
            AlertSoundEnabled = NoiseAlertEnabled
        };
        _studyAnalyticsService.UpdateConfig(newConfig);
    }

    private void UpdateThresholdText()
    {
        CurrentThresholdText = string.Format(
            CultureInfo.CurrentCulture,
            L("settings.study.current_threshold_format", "当前评分阈值: {0} dBFS"),
            NoiseSensitivityDbfs);
    }

    private void RefreshLocalizedText()
    {
        MasterSwitchHeader = L("settings.study.master_switch_header", "自习功能");
        MasterSwitchDescription = L("settings.study.master_switch_desc", "启用自习环境监测和专注计时功能。关闭后，相关组件将不会采集任何数据。");

        NoiseMonitoringHeader = L("settings.study.noise_header", "噪音监测");
        NoiseMonitoringDescription = L("settings.study.noise_description", "配置麦克风采集频率和噪音评分敏感度。");
        SamplingRateLabel = L("settings.study.sampling_rate_label", "采集频率");
        SamplingRateDescription = L("settings.study.sampling_rate_desc", "麦克风采集音频的时间间隔。更高的频率会更准确地捕捉噪音变化，但会增加电量消耗。");
        NoiseSensitivityLabel = L("settings.study.sensitivity_label", "噪音敏感度");
        NoiseSensitivityDescription = L("settings.study.sensitivity_desc", "评分阈值决定了什么级别的噪音会被认为是干扰。阈值越严格，越容易检测到轻微噪音。");

        FocusTimerHeader = L("settings.study.timer_header", "专注计时");
        FocusTimerDescription = L("settings.study.timer_description", "配置专注时段和休息时段的时长。");
        FocusDurationLabel = L("settings.study.focus_duration_label", "专注时长");
        FocusDurationDescription = L("settings.study.focus_duration_desc", "单次专注时段的持续时间（分钟）。");
        BreakDurationLabel = L("settings.study.break_duration_label", "休息时长");
        BreakDurationDescription = L("settings.study.break_duration_desc", "短休息时段的持续时间（分钟）。");
        LongBreakDurationLabel = L("settings.study.long_break_duration_label", "长休息时长");
        LongBreakDurationDescription = L("settings.study.long_break_duration_desc", "长休息时段的持续时间（分钟）。");
        SessionsBeforeLongBreakLabel = L("settings.study.sessions_before_long_break_label", "长休息间隔");
        SessionsBeforeLongBreakDescription = L("settings.study.sessions_before_long_break_desc", "经过几个专注时段后触发长休息。");
        AutoStartBreakLabel = L("settings.study.auto_start_break_label", "自动开始休息");
        AutoStartBreakDescription = L("settings.study.auto_start_break_desc", "专注时段结束后自动开始休息计时。");
        AutoStartFocusLabel = L("settings.study.auto_start_focus_label", "自动开始专注");
        AutoStartFocusDescription = L("settings.study.auto_start_focus_desc", "休息时段结束后自动开始专注计时。");

        AlertHeader = L("settings.study.alert_header", "提醒设置");
        AlertDescription = L("settings.study.alert_description", "配置噪音干扰提醒。");
        NoiseAlertEnabledLabel = L("settings.study.noise_alert_enabled_label", "启用噪音提醒");
        NoiseAlertEnabledDescription = L("settings.study.noise_alert_enabled_desc", "当检测到超过容忍阈值的噪音干扰时显示提醒。");
        MaxInterruptsPerMinuteLabel = L("settings.study.max_interrupts_label", "最大容忍打断次数");
        MaxInterruptsPerMinuteDescription = L("settings.study.max_interrupts_desc", "每分钟最多允许多少次噪音干扰事件，超过此值将触发提醒。");

        DisplayHeader = L("settings.study.display_header", "显示设置");
        DisplayDescription = L("settings.study.display_description", "配置噪音数据的显示方式。");
        ShowRealtimeDbLabel = L("settings.study.show_realtime_db_label", "显示实时分贝");
        ShowRealtimeDbDescription = L("settings.study.show_realtime_db_desc", "在组件中实时显示分贝值。");
        BaselineDbLabel = L("settings.study.baseline_db_label", "基准显示分贝");
        BaselineDbDescription = L("settings.study.baseline_db_desc", "校准后的显示分贝基准值，用于将 dBFS 转换为用户可读的 dB 值。");
        AvgWindowSecLabel = L("settings.study.avg_window_label", "平均时间窗");
        AvgWindowSecDescription = L("settings.study.avg_window_desc", "噪音平滑显示的时间窗口，较大的值会使显示更稳定但响应更慢。");

        FooterHint = L("settings.study.footer_hint", "这些设置将影响自习环境监测组件的行为。");

        UpdateThresholdText();
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed class PluginGeneratedSettingsPageViewModel
{
    public PluginGeneratedSettingsPageViewModel(
        ISettingsService settingsService,
        string pluginId,
        PluginSettingsSectionRegistration section,
        PluginLocalizer localizer)
    {
        SettingsService = settingsService;
        PluginId = pluginId;
        Section = section;
        Localizer = localizer;
        Title = localizer.GetString(section.TitleLocalizationKey, section.TitleLocalizationKey);
        Description = string.IsNullOrWhiteSpace(section.DescriptionLocalizationKey)
            ? null
            : localizer.GetString(section.DescriptionLocalizationKey, section.DescriptionLocalizationKey);
    }

    public ISettingsService SettingsService { get; }

    public string PluginId { get; }

    public PluginSettingsSectionRegistration Section { get; }

    public PluginLocalizer Localizer { get; }

    public string Title { get; }

    public string? Description { get; }
}

public sealed partial class DevSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public DevSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);

        RefreshLocalizedText();

        _isInitializing = true;
        LoadSettings();
        _isInitializing = false;

        _settingsFacade.Settings.Changed += OnSettingsChanged;
    }

    [ObservableProperty]
    private bool _isDevModeEnabled;

    [ObservableProperty]
    private string _devPluginPath = string.Empty;

    [ObservableProperty]
    private bool _enableThreeFingerSwipe;

    [ObservableProperty]
    private bool _enableFusedDesktop;

    [ObservableProperty]
    private bool _enableMainWindowDesktopLayer;

    [ObservableProperty]
    private string _infoBarTitle = string.Empty;

    [ObservableProperty]
    private string _infoBarMessage = string.Empty;

    [ObservableProperty]
    private string _devModeHeader = string.Empty;

    [ObservableProperty]
    private string _devModeDescription = string.Empty;

    [ObservableProperty]
    private string _threeFingerHeader = string.Empty;

    [ObservableProperty]
    private string _threeFingerDescription = string.Empty;

    [ObservableProperty]
    private string _fusedHeader = string.Empty;

    [ObservableProperty]
    private string _fusedDescription = string.Empty;

    [ObservableProperty]
    private string _mainWindowDesktopLayerHeader = string.Empty;

    [ObservableProperty]
    private string _mainWindowDesktopLayerDescription = string.Empty;

    [ObservableProperty]
    private string _desktopLayerConflictTitle = string.Empty;

    [ObservableProperty]
    private string _desktopLayerConflictEnableMainMessage = string.Empty;

    [ObservableProperty]
    private string _desktopLayerConflictEnableFusedMessage = string.Empty;

    [ObservableProperty]
    private string _desktopLayerConflictConfirmText = string.Empty;

    [ObservableProperty]
    private string _desktopLayerConflictCancelText = string.Empty;

    [ObservableProperty]
    private string _pluginPathHeader = string.Empty;

    [ObservableProperty]
    private string _pluginPathDescription = string.Empty;

    [ObservableProperty]
    private string _pluginPathPlaceholder = string.Empty;

    [ObservableProperty]
    private string _startupArgsHeader = string.Empty;

    [ObservableProperty]
    private string _startupArgsDescription = string.Empty;

    [ObservableProperty]
    private string _cliLabel = string.Empty;

    [ObservableProperty]
    private string _envLabel = string.Empty;

    [ObservableProperty]
    private string _otherArgsLabel = string.Empty;

    [ObservableProperty]
    private string _cliExample = string.Empty;

    [ObservableProperty]
    private string _envExample = string.Empty;

    [ObservableProperty]
    private string _otherDevModeLine = string.Empty;

    [ObservableProperty]
    private string _otherHotReloadLine = string.Empty;

    private void RefreshLocalizedText()
    {
        InfoBarTitle = L("settings.dev.infobar.title", "Preview and developer features");
        InfoBarMessage = L("settings.dev.infobar.message", "These options are intended for debugging and local plugin development.");
        DevModeHeader = L("settings.dev.mode_header", "Developer mode");
        DevModeDescription = L("settings.dev.mode_description", "Enable developer-focused startup helpers and diagnostics.");
        ThreeFingerHeader = L("settings.dev.three_finger_header", "Three-finger desktop swipe");
        ThreeFingerDescription = L("settings.dev.three_finger_description", "Enable desktop page switching gestures when supported.");
        FusedHeader = L("settings.dev.fused_header", "Fused desktop experience");
        FusedDescription = L("settings.dev.fused_description", "Enable the fused desktop shell and experimental entry points.");
        MainWindowDesktopLayerHeader = L("settings.dev.main_window_desktop_layer_header", "Prevent covering other apps");
        MainWindowDesktopLayerDescription = L("settings.dev.main_window_desktop_layer_description", "Keep the main desktop window on the desktop layer so ordinary app windows can stay above it.");
        DesktopLayerConflictTitle = L("settings.dev.desktop_layer_conflict_title", "Switch desktop layer mode?");
        DesktopLayerConflictEnableMainMessage = L("settings.dev.desktop_layer_conflict_enable_main", "Main desktop layer mode and fused desktop cannot run at the same time. Enabling this option will turn off fused desktop.");
        DesktopLayerConflictEnableFusedMessage = L("settings.dev.desktop_layer_conflict_enable_fused", "Fused desktop and main desktop layer mode cannot run at the same time. Enabling fused desktop will turn off main desktop layer mode.");
        DesktopLayerConflictConfirmText = L("settings.dev.desktop_layer_conflict_confirm", "Switch");
        DesktopLayerConflictCancelText = L("settings.dev.desktop_layer_conflict_cancel", "Cancel");
        PluginPathHeader = L("settings.dev.plugin_path_header", "Development plugin path");
        PluginPathDescription = L("settings.dev.plugin_path_description", "Load a local plugin output directory without packaging.");
        PluginPathPlaceholder = L("settings.dev.plugin_path_placeholder", "e.g. C:\\path\\to\\plugin\\bin\\Debug\\net10.0");
        StartupArgsHeader = L("settings.dev.startup_args_header", "Developer startup arguments");
        StartupArgsDescription = L("settings.dev.startup_args_description", "Command-line arguments and environment variables for development.");
        CliLabel = L("settings.dev.cli_label", "Command-line arguments:");
        EnvLabel = L("settings.dev.env_label", "Environment variables:");
        OtherArgsLabel = L("settings.dev.other_args_label", "Other arguments:");
        CliExample = L("settings.dev.cli_example", "--dev-plugin <path>   or -dp <path>");
        EnvExample = L("settings.dev.env_example", "LMD_DEV_PLUGIN=<path>");
        OtherDevModeLine = L("settings.dev.other_dev_mode", "--dev-mode / -dev     Enable developer mode startup helpers.");
        OtherHotReloadLine = L("settings.dev.other_hot_reload", "--hot-reload / -hr    Enable hot reload for development builds.");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);

    partial void OnIsDevModeEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        SaveField(nameof(AppSettingsSnapshot.IsDevModeEnabled), value);
    }

    partial void OnDevPluginPathChanged(string value)
    {
        if (_isInitializing) return;
        SaveField(nameof(AppSettingsSnapshot.DevPluginPath), value);
    }

    partial void OnEnableThreeFingerSwipeChanged(bool value)
    {
        if (_isInitializing) return;
        SaveField(nameof(AppSettingsSnapshot.EnableThreeFingerSwipe), value);
    }

    partial void OnEnableFusedDesktopChanged(bool value)
    {
        if (_isInitializing) return;
        SaveField(nameof(AppSettingsSnapshot.EnableFusedDesktop), value);
    }

    partial void OnEnableMainWindowDesktopLayerChanged(bool value)
    {
        if (_isInitializing) return;
        SaveField(nameof(AppSettingsSnapshot.EnableMainWindowDesktopLayer), value);
    }

    private void LoadSettings()
    {
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        IsDevModeEnabled = snapshot.IsDevModeEnabled;
        DevPluginPath = snapshot.DevPluginPath ?? string.Empty;
        EnableThreeFingerSwipe = snapshot.EnableThreeFingerSwipe;
        EnableFusedDesktop = snapshot.EnableFusedDesktop;
        EnableMainWindowDesktopLayer = snapshot.EnableMainWindowDesktopLayer;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        if (e.Scope != SettingsScope.App)
        {
            return;
        }

        var changedKeys = e.ChangedKeys?.ToArray();
        if (changedKeys is null || changedKeys.Length == 0)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            EnableThreeFingerSwipe = snapshot.EnableThreeFingerSwipe;
            EnableFusedDesktop = snapshot.EnableFusedDesktop;
            EnableMainWindowDesktopLayer = snapshot.EnableMainWindowDesktopLayer;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void SaveField<T>(string key, T value)
    {
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var property = typeof(AppSettingsSnapshot).GetProperty(key);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(snapshot, value);
        }

        _settingsFacade.Settings.SaveSnapshot(SettingsScope.App, snapshot, changedKeys: [key]);
    }

    public void ApplyFusedDesktopPreference(bool enabled, bool disableMainWindowDesktopLayer)
    {
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        snapshot.EnableFusedDesktop = enabled;
        if (enabled && disableMainWindowDesktopLayer)
        {
            snapshot.EnableMainWindowDesktopLayer = false;
        }

        SaveDesktopLayerPreferences(snapshot);
    }

    public void ApplyMainWindowDesktopLayerPreference(bool enabled, bool disableFusedDesktop)
    {
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        snapshot.EnableMainWindowDesktopLayer = enabled;
        if (enabled && disableFusedDesktop)
        {
            snapshot.EnableFusedDesktop = false;
        }

        SaveDesktopLayerPreferences(snapshot);
    }

    private void SaveDesktopLayerPreferences(AppSettingsSnapshot snapshot)
    {
        _settingsFacade.Settings.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.EnableFusedDesktop),
                nameof(AppSettingsSnapshot.EnableMainWindowDesktopLayer)
            ]);

        _isInitializing = true;
        try
        {
            EnableFusedDesktop = snapshot.EnableFusedDesktop;
            EnableMainWindowDesktopLayer = snapshot.EnableMainWindowDesktopLayer;
        }
        finally
        {
            _isInitializing = false;
        }
    }
}
