using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class WhiteboardComponentEditor : ComponentEditorViewBase
{
    private bool _suppressEvents;

    public WhiteboardComponentEditor()
        : this(null)
    {
    }

    public WhiteboardComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        BuildRetentionOptions();
        ApplyState();
    }

    private void BuildRetentionOptions()
    {
        RetentionComboBox.Items.Clear();
        for (var days = WhiteboardNoteRetentionPolicy.MinimumDays; days <= WhiteboardNoteRetentionPolicy.MaximumDays; days++)
        {
            var item = new ComboBoxItem
            {
                Tag = days.ToString(),
                Content = L(
                    "whiteboard.settings.retention.option",
                    "{0} days").Replace("{0}", days.ToString())
            };
            item.Classes.Add("component-editor-select-item");
            RetentionComboBox.Items.Add(item);
        }
    }

    private void ApplyState()
    {
        var snapshot = LoadSnapshot();
        var retentionDays = NormalizeRetentionDays(snapshot.WhiteboardNoteRetentionDays);

        HeadlineTextBlock.Text = Context?.Definition.DisplayName ?? "Blackboard";
        DescriptionTextBlock.Text = L(
            "whiteboard.settings.desc",
            "Each blackboard keeps its own note history and saves it independently.");
        RetentionHeaderTextBlock.Text = L(
            "whiteboard.settings.retention.title",
            "Note retention");
        RetentionDescriptionTextBlock.Text = L(
            "whiteboard.settings.retention.desc",
            "Choose how long this blackboard should keep saved notes before expired data is removed automatically.");
        InstanceHintTextBlock.Text = L(
            "whiteboard.settings.instance_scope",
            "This retention setting is stored per blackboard component instance.");

        _suppressEvents = true;
        RetentionComboBox.SelectedItem = RetentionComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                item.Tag is string tag &&
                int.TryParse(tag, out var days) &&
                days == retentionDays);
        _suppressEvents = false;
    }

    private void OnRetentionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var snapshot = LoadSnapshot();
        snapshot.WhiteboardNoteRetentionDays = GetSelectedRetentionDays();
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.WhiteboardNoteRetentionDays));
    }

    private int GetSelectedRetentionDays()
    {
        if (RetentionComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var days))
        {
            return NormalizeRetentionDays(days);
        }

        return WhiteboardNoteRetentionPolicy.DefaultDays;
    }

    private static int NormalizeRetentionDays(int days)
    {
        return WhiteboardNoteRetentionPolicy.NormalizeDays(
            days <= 0
                ? WhiteboardNoteRetentionPolicy.DefaultDays
                : days);
    }
}
