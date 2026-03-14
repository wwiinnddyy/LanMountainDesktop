using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;

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
        if (!showDisplayDb && !showDbfs)
        {
            showDisplayDb = true;
        }

        HeadlineTextBlock.Text = Context?.Definition.DisplayName ?? "Study Environment";
        DescriptionTextBlock.Text = L("study.environment.settings.desc", "配置右侧实时噪音值显示内容。");
        DisplayDbToggleSwitch.Content = L("study.environment.settings.show_display_db", "显示 display dB");
        DbfsToggleSwitch.Content = L("study.environment.settings.show_dbfs", "显示 dBFS");
        HintTextBlock.Text = L("study.environment.settings.hint", "至少启用一种显示方式。");

        _suppressEvents = true;
        DisplayDbToggleSwitch.IsChecked = showDisplayDb;
        DbfsToggleSwitch.IsChecked = showDbfs;
        _suppressEvents = false;
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
