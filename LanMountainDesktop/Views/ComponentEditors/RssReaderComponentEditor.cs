using Avalonia.Controls;
using Avalonia.Layout;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services.RssReader;

namespace LanMountainDesktop.Views.ComponentEditors;

public sealed class RssReaderComponentEditor : ComponentEditorViewBase, IDisposable
{
    private readonly RssReaderService _service = new();
    private readonly ComboBox _sourceCombo = new();
    private readonly CheckBox _unreadFirst = new();
    private readonly NumericUpDown _displayCount = new() { Minimum = 5, Maximum = 100, Increment = 5 };

    public RssReaderComponentEditor(DesktopComponentEditorContext? context) : base(context)
    {
        var snapshot = LoadSnapshot();
        var options = new List<SourceOption> { new(string.Empty, L("rss.all_sources", "All sources")) };
        options.AddRange(_service.GetSources().Select(source => new SourceOption(source.Id, source.Title)));
        _sourceCombo.ItemsSource = options;
        _sourceCombo.SelectedItem = options.FirstOrDefault(option => option.Id == snapshot.RssReaderSourceId) ?? options[0];
        _unreadFirst.IsChecked = snapshot.RssReaderUnreadFirst;
        _displayCount.Value = Math.Clamp(snapshot.RssReaderDisplayCount, 5, 100);

        _unreadFirst.Content = L("rss.unread_first", "Show unread entries first");
        var save = new Button { Content = L("rss.save", "Save"), HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => Save();
        Content = new StackPanel
        {
            Spacing = 14,
            Margin = new Avalonia.Thickness(20),
            Children =
            {
                new TextBlock { Text = L("component.rss_reader", "RSS Reader"), FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = L("rss.instance_settings_description", "These display options apply only to this component instance."), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.7 },
                new TextBlock { Text = L("rss.source", "Source") }, _sourceCombo,
                _unreadFirst,
                new TextBlock { Text = L("rss.entry_count", "Number of entries") }, _displayCount,
                save
            }
        };
    }

    public void Dispose() => _service.Dispose();

    private void Save()
    {
        var snapshot = LoadSnapshot();
        snapshot.RssReaderSourceId = (_sourceCombo.SelectedItem as SourceOption)?.Id ?? string.Empty;
        snapshot.RssReaderUnreadFirst = _unreadFirst.IsChecked == true;
        snapshot.RssReaderDisplayCount = (int)(_displayCount.Value ?? 20);
        SaveSnapshot(snapshot,
            nameof(ComponentSettingsSnapshot.RssReaderSourceId),
            nameof(ComponentSettingsSnapshot.RssReaderUnreadFirst),
            nameof(ComponentSettingsSnapshot.RssReaderDisplayCount));
    }

    private sealed record SourceOption(string Id, string Title)
    {
        public override string ToString() => Title;
    }
}
