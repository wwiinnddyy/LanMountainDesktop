using System;
using Avalonia.Controls;
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
        DescriptionTextBlock.Text = L(
            "study.environment.settings.desc",
            "Configure the realtime audio level information shown on the right side.");

        ColorSchemeHeaderTextBlock.Text = L("component.settings.color_scheme", "Color Scheme");
        FollowSystemColorSchemeItem.Content = L("component.color_scheme.follow_system", "Follow system color scheme");
        UseNativeColorSchemeItem.Content = L("component.color_scheme.native", "Use component custom color scheme");

        DisplayDbToggleSwitch.Content = L("study.environment.settings.show_display_db", "Show display dB");
        DbfsToggleSwitch.Content = L("study.environment.settings.show_dbfs", "Show dBFS");
        HintTextBlock.Text = L("study.environment.settings.hint", "At least one display mode must stay enabled.");

        _suppressEvents = true;
        ColorSchemeComboBox.SelectedItem =
            string.IsNullOrEmpty(colorSchemeSource) ||
            string.Equals(colorSchemeSource, ThemeAppearanceValues.ColorSchemeFollowSystem, StringComparison.OrdinalIgnoreCase)
                ? FollowSystemColorSchemeItem
                : UseNativeColorSchemeItem;
        DisplayDbToggleSwitch.IsChecked = showDisplayDb;
        DbfsToggleSwitch.IsChecked = showDbfs;
        _suppressEvents = false;
    }

    private void OnColorSchemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var colorSchemeSource = ColorSchemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            ? tag
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
