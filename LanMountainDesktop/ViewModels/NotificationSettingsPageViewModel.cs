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
    private bool _isInitializing;

    public NotificationSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));

        Positions = CreatePositionOptions();
        Durations = CreateDurationOptions();
        TestPositions = CreatePositionOptions();
        TestSeverities = CreateSeverityOptions();

        LoadSettings();

        // Initialize test selections
        SelectedTestPosition = TestPositions[1]; // TopRight
        SelectedTestSeverity = TestSeverities[0]; // Info
        TestDurationSeconds = 4; // Default 4 seconds
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

    private static ObservableCollection<SelectionOption> CreatePositionOptions()
    {
        return
        [
            new SelectionOption("TopLeft", "左上角"),
            new SelectionOption("TopRight", "右上角"),
            new SelectionOption("TopCenter", "正上方"),
            new SelectionOption("BottomLeft", "左下角"),
            new SelectionOption("BottomRight", "右下角"),
            new SelectionOption("BottomCenter", "正下方"),
            new SelectionOption("Center", "正中央")
        ];
    }

    private static ObservableCollection<SelectionOption> CreateDurationOptions()
    {
        return
        [
            new SelectionOption("2", "2 秒"),
            new SelectionOption("4", "4 秒"),
            new SelectionOption("6", "6 秒"),
            new SelectionOption("8", "8 秒"),
            new SelectionOption("10", "10 秒")
        ];
    }

    private static ObservableCollection<SelectionOption> CreateSeverityOptions()
    {
        return
        [
            new SelectionOption("Info", "信息"),
            new SelectionOption("Success", "成功"),
            new SelectionOption("Warning", "警告"),
            new SelectionOption("Error", "错误")
        ];
    }

    [ObservableProperty] private string _notificationHeader = "通知";
    [ObservableProperty] private string _enableNotificationHeader = "启用通知";
    [ObservableProperty] private string _enableNotificationDescription = "开启或关闭全局通知功能";
    [ObservableProperty] private string _defaultPositionHeader = "默认位置";
    [ObservableProperty] private string _defaultPositionDescription = "通知弹出的默认位置";
    [ObservableProperty] private string _durationHeader = "显示时长";
    [ObservableProperty] private string _durationDescription = "通知自动关闭的时间";
    [ObservableProperty] private string _behaviorHeader = "行为";
    [ObservableProperty] private string _hoverPauseHeader = "悬停暂停";
    [ObservableProperty] private string _hoverPauseDescription = "鼠标悬停时暂停自动关闭计时";
    [ObservableProperty] private string _clickCloseHeader = "点击关闭";
    [ObservableProperty] private string _clickCloseDescription = "点击通知后关闭";
    [ObservableProperty] private string _maxNotificationsHeader = "最大数量";
    [ObservableProperty] private string _maxNotificationsDescription = "每个位置最多显示的通知数量";
    [ObservableProperty] private string _testHeader = "测试";
    [ObservableProperty] private string _testNotificationHeader = "测试通知";
    [ObservableProperty] private string _testNotificationDescription = "选择位置和类型，发送测试通知";
    [ObservableProperty] private string _sendTestButtonText = "发送";

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

        var (title, message) = severity! switch
        {
            "Info" => ("测试通知", "这是一条信息类型的通知"),
            "Success" => ("操作成功", "任务已完成"),
            "Warning" => ("警告提示", "请注意检查"),
            "Error" => ("错误报告", "操作失败，请重试"),
            _ => ("测试通知", "这是一条测试通知")
        };

        // Create notification content with specified duration
        var content = new NotificationContent(
            Title: title,
            Message: message,
            Severity: Enum.Parse<NotificationSeverity>(severity),
            Position: position,
            Duration: TimeSpan.FromSeconds(TestDurationSeconds));

        // Use Show method which will automatically route to dialog or toast based on position
        App.CurrentNotificationService?.Show(content);
    }
}
