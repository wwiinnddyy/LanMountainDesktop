using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ViewModels;

public sealed partial class NotificationSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public NotificationSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);

        Positions = CreatePositionOptions();
        Durations = CreateDurationOptions();
        TestPositions = CreatePositionOptions();
        TestSeverities = CreateSeverityOptions();
        RefreshLocalizedText();

        LoadSettings();

        SelectedTestPosition = TestPositions[1];
        SelectedTestSeverity = TestSeverities[0];
        TestDurationSeconds = 4;
    }

    private void LoadSettings()
    {
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);

        _isInitializing = true;

        IsNotificationEnabled = snapshot.NotificationEnabled;
        IsHoverPauseEnabled = snapshot.NotificationHoverPauseEnabled;
        IsClickCloseEnabled = snapshot.NotificationClickCloseEnabled;
        MaxNotificationsPerPosition = snapshot.NotificationMaxPerPosition;

        SelectedPosition = Positions.FirstOrDefault(p =>
                string.Equals(p.Value, snapshot.NotificationDefaultPosition, StringComparison.OrdinalIgnoreCase))
            ?? Positions[1];

        SelectedDuration = Durations.FirstOrDefault(d =>
                int.TryParse(d.Value, out var seconds) && seconds == snapshot.NotificationDurationSeconds)
            ?? Durations[1];

        _isInitializing = false;
    }

    private void SaveSettings()
    {
        if (_isInitializing) return;

        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);

        snapshot.NotificationEnabled = IsNotificationEnabled;
        snapshot.NotificationDefaultPosition = SelectedPosition?.Value ?? "TopRight";
        snapshot.NotificationDurationSeconds = int.TryParse(SelectedDuration?.Value, out var seconds) ? seconds : 4;
        snapshot.NotificationHoverPauseEnabled = IsHoverPauseEnabled;
        snapshot.NotificationClickCloseEnabled = IsClickCloseEnabled;
        snapshot.NotificationMaxPerPosition = MaxNotificationsPerPosition;

        _settingsFacade.Settings.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.NotificationEnabled),
                nameof(AppSettingsSnapshot.NotificationDefaultPosition),
                nameof(AppSettingsSnapshot.NotificationDurationSeconds),
                nameof(AppSettingsSnapshot.NotificationHoverPauseEnabled),
                nameof(AppSettingsSnapshot.NotificationClickCloseEnabled),
                nameof(AppSettingsSnapshot.NotificationMaxPerPosition)
            ]);
    }

    private ObservableCollection<SelectionOption> CreatePositionOptions()
    {
        return
        [
            new SelectionOption("TopLeft", L("settings.notifications.position.top_left", "Top left")),
            new SelectionOption("TopRight", L("settings.notifications.position.top_right", "Top right")),
            new SelectionOption("TopCenter", L("settings.notifications.position.top_center", "Top center")),
            new SelectionOption("BottomLeft", L("settings.notifications.position.bottom_left", "Bottom left")),
            new SelectionOption("BottomRight", L("settings.notifications.position.bottom_right", "Bottom right")),
            new SelectionOption("BottomCenter", L("settings.notifications.position.bottom_center", "Bottom center")),
            new SelectionOption("Center", L("settings.notifications.position.center", "Center"))
        ];
    }

    private ObservableCollection<SelectionOption> CreateDurationOptions()
    {
        return
        [
            new SelectionOption("2", L("settings.notifications.duration.2s", "2 seconds")),
            new SelectionOption("4", L("settings.notifications.duration.4s", "4 seconds")),
            new SelectionOption("6", L("settings.notifications.duration.6s", "6 seconds")),
            new SelectionOption("8", L("settings.notifications.duration.8s", "8 seconds")),
            new SelectionOption("10", L("settings.notifications.duration.10s", "10 seconds"))
        ];
    }

    private ObservableCollection<SelectionOption> CreateSeverityOptions()
    {
        return
        [
            new SelectionOption("Info", L("settings.notifications.severity.info", "Info")),
            new SelectionOption("Success", L("settings.notifications.severity.success", "Success")),
            new SelectionOption("Warning", L("settings.notifications.severity.warning", "Warning")),
            new SelectionOption("Error", L("settings.notifications.severity.error", "Error"))
        ];
    }

    private void RefreshLocalizedText()
    {
        NotificationHeader = L("settings.notifications.section_header", "Notifications");
        EnableNotificationHeader = L("settings.notifications.enable_header", "Enable notifications");
        EnableNotificationDescription = L("settings.notifications.enable_desc", "Turn all notification toasts on or off.");
        BehaviorHeader = L("settings.notifications.behavior_header", "Behavior");
        HoverPauseHeader = L("settings.notifications.hover_pause_header", "Pause on hover");
        HoverPauseDescription = L("settings.notifications.hover_pause_desc", "Pause auto-dismiss while hovering.");
        ClickCloseHeader = L("settings.notifications.click_close_header", "Close on click");
        ClickCloseDescription = L("settings.notifications.click_close_desc", "Dismiss when clicked.");
        MaxNotificationsHeader = L("settings.notifications.max_header", "Max per position");
        MaxNotificationsDescription = L("settings.notifications.max_desc", "Maximum notifications per corner or edge.");
        TestHeader = L("settings.notifications.test_header", "Test");
        TestNotificationHeader = L("settings.notifications.test_notification_header", "Test notification");
        TestNotificationDescription = L("settings.notifications.test_notification_desc", "Send a sample notification.");
        DefaultPositionHeader = L("settings.notifications.default_position_header", "Default position");
        DefaultPositionDescription = L("settings.notifications.default_position_desc", "Where notifications appear first.");
        DurationHeader = L("settings.notifications.duration_header", "Visible duration");
        DurationDescription = L("settings.notifications.duration_desc", "How long notifications stay on screen.");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);

    [ObservableProperty] private string _notificationHeader = string.Empty;

    [ObservableProperty] private string _enableNotificationHeader = string.Empty;

    [ObservableProperty] private string _enableNotificationDescription = string.Empty;

    [ObservableProperty] private string _defaultPositionHeader = string.Empty;

    [ObservableProperty] private string _defaultPositionDescription = string.Empty;

    [ObservableProperty] private string _durationHeader = string.Empty;

    [ObservableProperty] private string _durationDescription = string.Empty;

    [ObservableProperty] private string _behaviorHeader = string.Empty;

    [ObservableProperty] private string _hoverPauseHeader = string.Empty;

    [ObservableProperty] private string _hoverPauseDescription = string.Empty;

    [ObservableProperty] private string _clickCloseHeader = string.Empty;

    [ObservableProperty] private string _clickCloseDescription = string.Empty;

    [ObservableProperty] private string _maxNotificationsHeader = string.Empty;

    [ObservableProperty] private string _maxNotificationsDescription = string.Empty;

    [ObservableProperty] private string _testHeader = string.Empty;

    [ObservableProperty] private string _testNotificationHeader = string.Empty;

    [ObservableProperty] private string _testNotificationDescription = string.Empty;

    [ObservableProperty] private bool _isNotificationEnabled = true;

    [ObservableProperty] private bool _isHoverPauseEnabled = true;

    [ObservableProperty] private bool _isClickCloseEnabled = true;

    [ObservableProperty] private int _maxNotificationsPerPosition = 5;

    [ObservableProperty] private SelectionOption? _selectedPosition;

    [ObservableProperty] private SelectionOption? _selectedDuration;

    [ObservableProperty] private SelectionOption? _selectedTestPosition;

    [ObservableProperty] private SelectionOption? _selectedTestSeverity;

    [ObservableProperty] private int _testDurationSeconds = 4;

    public ObservableCollection<SelectionOption> Positions { get; }
    public ObservableCollection<SelectionOption> Durations { get; }
    public ObservableCollection<SelectionOption> TestPositions { get; }
    public ObservableCollection<SelectionOption> TestSeverities { get; }

    partial void OnIsNotificationEnabledChanged(bool value) => SaveSettings();

    partial void OnIsHoverPauseEnabledChanged(bool value) => SaveSettings();

    partial void OnIsClickCloseEnabledChanged(bool value) => SaveSettings();

    partial void OnMaxNotificationsPerPositionChanged(int value) => SaveSettings();

    partial void OnSelectedPositionChanged(SelectionOption? value) => SaveSettings();

    partial void OnSelectedDurationChanged(SelectionOption? value) => SaveSettings();

    [RelayCommand]
    private void SendTest()
    {
        if (SelectedTestPosition is null || SelectedTestSeverity is null)
            return;

        var position = Enum.Parse<NotificationPosition>(SelectedTestPosition.Value);
        var severity = SelectedTestSeverity.Value;

        var (title, message) = severity switch
        {
            "Info" => (
                L("settings.notifications.test.title_info", "Test notification"),
                L("settings.notifications.test.message_info", "This is an informational test notification.")),
            "Success" => (
                L("settings.notifications.test.title_success", "Succeeded"),
                L("settings.notifications.test.message_success", "The task completed successfully.")),
            "Warning" => (
                L("settings.notifications.test.title_warning", "Warning"),
                L("settings.notifications.test.message_warning", "Please review this notice.")),
            "Error" => (
                L("settings.notifications.test.title_error", "Error"),
                L("settings.notifications.test.message_error", "Something went wrong. Please try again.")),
            _ => (
                L("settings.notifications.test.title_default", "Test notification"),
                L("settings.notifications.test.message_default", "This is a test notification."))
        };

        var content = new NotificationContent(
            Title: title,
            Message: message,
            Severity: Enum.Parse<NotificationSeverity>(severity!),
            Position: position,
            Duration: TimeSpan.FromSeconds(TestDurationSeconds));

        App.CurrentNotificationService?.Show(content);
    }
}
