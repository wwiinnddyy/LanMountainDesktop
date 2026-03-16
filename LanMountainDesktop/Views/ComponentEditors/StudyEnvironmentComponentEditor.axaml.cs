using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class StudyEnvironmentComponentEditor : ComponentEditorViewBase
{
    private bool _suppressEvents;

    public StudyEnvironmentComponentEditor()
        : this(null)
    {
    }

    public StudyEnvironmentComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        ApplyState();
    }

    private void ApplyState()
    {
        var snapshot = LoadSnapshot();
        var showDisplayDb = snapshot.StudyEnvironmentShowDisplayDb;
        var showDbfs = snapshot.StudyEnvironmentShowDbfs;
        var colorSchemeSource = snapshot.ColorSchemeSource;

        if (!showDisplayDb && !showDbfs)
        {
            showDisplayDb = true;
        }

        HeadlineTextBlock.Text = Context?.Definition.DisplayName ?? "Study Environment";
        DescriptionTextBlock.Text = L("study.environment.settings.desc", "配置右侧实时噪音值显示内容。");
        
        ColorSchemeHeaderTextBlock.Text = L("component.settings.color_scheme", "配色方案");
        FollowSystemRadioButton.Content = L("component.color_scheme.follow_system", "跟随系统配色");
        UseNativeRadioButton.Content = L("component.color_scheme.native", "使用组件自定义配色");
        
        DisplayDbToggleSwitch.Content = L("study.environment.settings.show_display_db", "显示 display dB");
        DbfsToggleSwitch.Content = L("study.environment.settings.show_dbfs", "显示 dBFS");
        HintTextBlock.Text = L("study.environment.settings.hint", "至少启用一种显示方式。");

        _suppressEvents = true;
        
        if (string.IsNullOrEmpty(colorSchemeSource) || 
            colorSchemeSource == ThemeAppearanceValues.ColorSchemeFollowSystem)
        {
            FollowSystemRadioButton.IsChecked = true;
        }
        else
        {
            UseNativeRadioButton.IsChecked = true;
        }
        
        DisplayDbToggleSwitch.IsChecked = showDisplayDb;
        DbfsToggleSwitch.IsChecked = showDbfs;
        _suppressEvents = false;
    }

    private void OnColorSchemeChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var useNative = UseNativeRadioButton.IsChecked == true;
        var colorSchemeSource = useNative 
            ? ThemeAppearanceValues.ColorSchemeNative 
            : ThemeAppearanceValues.ColorSchemeFollowSystem;

        var snapshot = LoadSnapshot();
        snapshot.ColorSchemeSource = colorSchemeSource;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ColorSchemeSource));
    }

    private void OnToggleChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var showDisplayDb = DisplayDbToggleSwitch.IsChecked == true;
        var showDbfs = DbfsToggleSwitch.IsChecked == true;
        if (!showDisplayDb && !showDbfs)
        {
            _suppressEvents = true;
            DisplayDbToggleSwitch.IsChecked = true;
            _suppressEvents = false;
            showDisplayDb = true;
        }

        var snapshot = LoadSnapshot();
        snapshot.StudyEnvironmentShowDisplayDb = showDisplayDb;
        snapshot.StudyEnvironmentShowDbfs = showDbfs;
        SaveSnapshot(
            snapshot,
            nameof(ComponentSettingsSnapshot.StudyEnvironmentShowDisplayDb),
            nameof(ComponentSettingsSnapshot.StudyEnvironmentShowDbfs));
    }
}
