using System;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class DailyArtworkComponentEditor : ComponentEditorViewBase
{
    private bool _suppressEvents;

    public DailyArtworkComponentEditor()
        : this(null)
    {
    }

    public DailyArtworkComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        ApplyState();
    }

    private void ApplyState()
    {
        SourceLabelTextBlock.Text = L("artwork.settings.source_label", "Mirror Source");
        DomesticItem.Content = L("artwork.settings.source_domestic", "Domestic Mirror");
        OverseasItem.Content = L("artwork.settings.source_overseas", "Overseas Mirror");

        _suppressEvents = true;
        var source = DailyArtworkMirrorSources.Normalize(LoadSnapshot().DailyArtworkMirrorSource);
        SourceComboBox.SelectedItem = string.Equals(source, DailyArtworkMirrorSources.Domestic, StringComparison.OrdinalIgnoreCase)
            ? DomesticItem
            : OverseasItem;
        UpdateStatus(source);
        _suppressEvents = false;
    }

    private void OnSourceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var source = SourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            ? DailyArtworkMirrorSources.Normalize(tag)
            : DailyArtworkMirrorSources.Overseas;

        var snapshot = LoadSnapshot();
        snapshot.DailyArtworkMirrorSource = source;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.DailyArtworkMirrorSource));
        UpdateStatus(source);
    }

    private void UpdateStatus(string source)
    {
        StatusTextBlock.Text = string.Equals(source, DailyArtworkMirrorSources.Domestic, StringComparison.OrdinalIgnoreCase)
            ? L("artwork.settings.source_status_domestic", "当前源：国内镜像（优先中国网络）")
            : L("artwork.settings.source_status_overseas", "当前源：国外镜像（艺术馆推荐）");
    }
}
