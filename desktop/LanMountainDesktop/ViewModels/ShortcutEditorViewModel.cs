using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.ViewModels;

public sealed partial class ShortcutEditorViewModel : ViewModelBase
{
    private readonly DesktopComponentEditorContext? _context;
    private bool _isInitializing;

    public ShortcutEditorViewModel(DesktopComponentEditorContext? context)
    {
        _context = context;

        ClickModeOptions = new ObservableCollection<SelectionOption>
        {
            new("Double", "双击打开"),
            new("Single", "单击打开")
        };

        LoadSettings();
    }

    private void LoadSettings()
    {
        var snapshot = _context?.ComponentSettingsAccessor.LoadSnapshot<ComponentSettingsSnapshot>()
            ?? new ComponentSettingsSnapshot();

        _isInitializing = true;

        TargetPath = snapshot.ShortcutTargetPath ?? string.Empty;
        SelectedClickMode = ClickModeOptions.FirstOrDefault(o => o.Value == snapshot.ShortcutClickMode)
            ?? ClickModeOptions[0];
        ShowBackground = snapshot.ShortcutShowBackground;

        _isInitializing = false;
    }

    private void SaveSettings()
    {
        if (_isInitializing || _context == null) return;

        var snapshot = _context.ComponentSettingsAccessor.LoadSnapshot<ComponentSettingsSnapshot>();

        snapshot.ShortcutTargetPath = string.IsNullOrWhiteSpace(TargetPath) ? null : TargetPath;
        snapshot.ShortcutClickMode = SelectedClickMode?.Value ?? "Double";
        snapshot.ShortcutShowBackground = ShowBackground;

        _context.ComponentSettingsAccessor.SaveSnapshot(snapshot);

        _context.HostContext.RequestRefresh();
    }

    [ObservableProperty] private string _descriptionText = "配置此快捷方式组件的目标路径和打开方式。这些设置仅作用于当前组件实例。";
    [ObservableProperty] private string _targetPathLabel = "目标路径";
    [ObservableProperty] private string _targetPathPlaceholder = "未选择目标";
    [ObservableProperty] private string _browseButtonText = "浏览...";
    [ObservableProperty] private string _clearButtonText = "清除";
    [ObservableProperty] private string _clickModeLabel = "打开方式";
    [ObservableProperty] private string _backgroundLabel = "显示背景";
    [ObservableProperty] private string _backgroundDescription = "关闭后组件背景将变为透明。";

    [ObservableProperty] private string _targetPath = string.Empty;
    [ObservableProperty] private SelectionOption? _selectedClickMode;
    [ObservableProperty] private bool _showBackground = true;

    public ObservableCollection<SelectionOption> ClickModeOptions { get; }

    public void SetTargetPath(string? path)
    {
        TargetPath = path ?? string.Empty;
        SaveSettings();
    }

    public void ClearTargetPath()
    {
        TargetPath = string.Empty;
        SaveSettings();
    }

    partial void OnSelectedClickModeChanged(SelectionOption? value) => SaveSettings();
    partial void OnShowBackgroundChanged(bool value) => SaveSettings();
}
