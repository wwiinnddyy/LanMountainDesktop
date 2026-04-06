using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.ViewModels;

public sealed partial class NotificationBoxEditorViewModel : ViewModelBase
{
    private readonly DesktopComponentEditorContext? _context;
    private bool _isInitializing;

    public NotificationBoxEditorViewModel(DesktopComponentEditorContext? context)
    {
        _context = context;

        MaxDisplayCountOptions = new ObservableCollection<SelectionOption>
        {
            new("20", "20条"),
            new("50", "50条"),
            new("100", "100条"),
            new("200", "200条")
        };

        SortOrderOptions = new ObservableCollection<SelectionOption>
        {
            new("TimeDesc", "最新优先"),
            new("TimeAsc", "最早优先"),
            new("AppGroup", "按应用分组")
        };

        TimeFormatOptions = new ObservableCollection<SelectionOption>
        {
            new("Relative", "相对时间（如：5分钟前）"),
            new("Absolute", "绝对时间（如：14:30）")
        };

        LoadSettings();
    }

    private void LoadSettings()
    {
        var snapshot = _context?.ComponentSettingsAccessor.LoadSnapshot<ComponentSettingsSnapshot>()
            ?? new ComponentSettingsSnapshot();

        _isInitializing = true;

        var countValue = snapshot.NotificationBoxMaxDisplayCount.ToString();
        SelectedMaxDisplayCount = MaxDisplayCountOptions.FirstOrDefault(o => o.Value == countValue)
            ?? MaxDisplayCountOptions[1]; // 默认50

        SelectedSortOrder = SortOrderOptions.FirstOrDefault(o => o.Value == snapshot.NotificationBoxSortOrder)
            ?? SortOrderOptions[0];
        ShowAppIcon = snapshot.NotificationBoxShowAppIcon;
        ShowTimestamp = snapshot.NotificationBoxShowTimestamp;
        SelectedTimeFormat = TimeFormatOptions.FirstOrDefault(o => o.Value == snapshot.NotificationBoxTimeFormat)
            ?? TimeFormatOptions[0];
        GroupByApp = snapshot.NotificationBoxGroupByApp;
        ShowClearButton = snapshot.NotificationBoxShowClearButton;

        _isInitializing = false;
    }

    private void SaveSettings()
    {
        if (_isInitializing || _context == null) return;

        var snapshot = _context.ComponentSettingsAccessor.LoadSnapshot<ComponentSettingsSnapshot>();

        snapshot.NotificationBoxMaxDisplayCount = int.TryParse(SelectedMaxDisplayCount?.Value, out var count) ? count : 50;
        snapshot.NotificationBoxSortOrder = SelectedSortOrder?.Value ?? "TimeDesc";
        snapshot.NotificationBoxShowAppIcon = ShowAppIcon;
        snapshot.NotificationBoxShowTimestamp = ShowTimestamp;
        snapshot.NotificationBoxTimeFormat = SelectedTimeFormat?.Value ?? "Relative";
        snapshot.NotificationBoxGroupByApp = GroupByApp;
        snapshot.NotificationBoxShowClearButton = ShowClearButton;

        _context.ComponentSettingsAccessor.SaveSnapshot(snapshot);

        _context.HostContext.RequestRefresh();
    }

    [ObservableProperty] private string _descriptionText = "配置此消息盒子组件的显示方式。这些设置仅作用于当前组件实例。";
    [ObservableProperty] private string _maxDisplayCountLabel = "最大显示数量";
    [ObservableProperty] private string _maxDisplayCountDescription = "组件中最多显示的通知条数";
    [ObservableProperty] private string _sortOrderLabel = "排序方式";
    [ObservableProperty] private string _displayOptionsLabel = "显示选项";
    [ObservableProperty] private string _showAppIconLabel = "显示应用图标";
    [ObservableProperty] private string _showTimestampLabel = "显示时间戳";
    [ObservableProperty] private string _groupByAppLabel = "按应用分组显示";
    [ObservableProperty] private string _showClearButtonLabel = "显示清空按钮";
    [ObservableProperty] private string _timeFormatLabel = "时间格式";

    [ObservableProperty] private SelectionOption? _selectedMaxDisplayCount;
    [ObservableProperty] private SelectionOption? _selectedSortOrder;
    [ObservableProperty] private bool _showAppIcon = true;
    [ObservableProperty] private bool _showTimestamp = true;
    [ObservableProperty] private SelectionOption? _selectedTimeFormat;
    [ObservableProperty] private bool _groupByApp = false;
    [ObservableProperty] private bool _showClearButton = true;

    public ObservableCollection<SelectionOption> MaxDisplayCountOptions { get; }
    public ObservableCollection<SelectionOption> SortOrderOptions { get; }
    public ObservableCollection<SelectionOption> TimeFormatOptions { get; }

    partial void OnSelectedMaxDisplayCountChanged(SelectionOption? value) => SaveSettings();
    partial void OnSelectedSortOrderChanged(SelectionOption? value) => SaveSettings();
    partial void OnShowAppIconChanged(bool value) => SaveSettings();
    partial void OnShowTimestampChanged(bool value) => SaveSettings();
    partial void OnSelectedTimeFormatChanged(SelectionOption? value) => SaveSettings();
    partial void OnGroupByAppChanged(bool value) => SaveSettings();
    partial void OnShowClearButtonChanged(bool value) => SaveSettings();
}
