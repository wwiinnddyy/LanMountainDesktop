using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class RemovableStorageComponentEditor : ComponentEditorViewBase
{
    private bool _suppressEvents;

    public RemovableStorageComponentEditor()
        : this(null)
    {
    }

    public RemovableStorageComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        ApplyState();
    }

    private void ApplyState()
    {
        var snapshot = LoadSnapshot();
        var colorSchemeSource = snapshot.ColorSchemeSource;

        HeadlineTextBlock.Text = Context?.Definition.DisplayName ?? "Removable Storage";
        DescriptionTextBlock.Text = L(
            "removable_storage.settings.desc",
            "Show a connected USB drive with quick open and eject actions.");
        ColorSchemeHeaderTextBlock.Text = L("component.settings.color_scheme", "Color Scheme");
        FollowSystemColorSchemeItem.Content = L("component.color_scheme.follow_system", "Follow system color scheme");
        UseNativeColorSchemeItem.Content = L("component.color_scheme.native", "Use component custom color scheme");
        BehaviorHeaderTextBlock.Text = L("removable_storage.settings.behavior_title", "Behavior");
        BehaviorTextBlock.Text = L(
            "removable_storage.settings.behavior_desc",
            "The widget automatically watches for removable drives and switches to the newest inserted USB drive.");

        _suppressEvents = true;
        ColorSchemeComboBox.SelectedItem =
            string.IsNullOrWhiteSpace(colorSchemeSource) ||
            string.Equals(colorSchemeSource, ThemeAppearanceValues.ColorSchemeFollowSystem, StringComparison.OrdinalIgnoreCase)
                ? FollowSystemColorSchemeItem
                : UseNativeColorSchemeItem;
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

        var snapshot = LoadSnapshot();
        snapshot.ColorSchemeSource = ColorSchemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            ? tag
            : ThemeAppearanceValues.ColorSchemeFollowSystem;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ColorSchemeSource));
    }
}
