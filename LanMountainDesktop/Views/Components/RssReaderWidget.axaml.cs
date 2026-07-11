using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System.Globalization;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.RssReader;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Views.Components;

public sealed partial class RssReaderWidget : UserControl, IDesktopComponentWidget, IDesktopComponentLifecycleWidget
{
    private readonly RssReaderService _service;
    private readonly IComponentSettingsAccessor _settingsAccessor;
    private readonly string? _placementId;
    private readonly LocalizationService _localization = new();
    private readonly string _languageCode;
    private readonly DispatcherTimer _refreshTimer = new();
    private bool _isRefreshing;

    public RssReaderWidget(IComponentSettingsAccessor settingsAccessor, string? placementId)
    {
        _settingsAccessor = settingsAccessor;
        _placementId = placementId;
        _languageCode = _localization.NormalizeLanguageCode(HostSettingsFacadeProvider.GetOrCreate().Region.Get().LanguageCode);
        _service = new RssReaderService();
        InitializeComponent();
        ApplyLocalization();
        OpenReaderButton.Click += OnOpen;
        EmptyButton.Click += OnOpen;
        RefreshButton.Click += OnRefresh;
        MarkAllReadButton.Click += OnMarkAllRead;
        _service.Changed += OnServiceChanged;
        _refreshTimer.Tick += OnRefreshTimer;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    public void ApplyCellSize(double cellSize) { }

    public void OnWidgetDestroyed() => DisposeResources();

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Reload();
        ConfigureTimer();
        _ = RefreshAsync(force: true);
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => _refreshTimer.Stop();

    private void ConfigureTimer()
    {
        var minutes = _service.GetSettings().RefreshIntervalMinutes;
        if (minutes <= 0) { _refreshTimer.Stop(); return; }
        _refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
        _refreshTimer.Start();
    }

    private async void OnRefreshTimer(object? sender, EventArgs e) => await RefreshAsync(force: false);
    private async void OnRefresh(object? sender, RoutedEventArgs e) => await RefreshAsync(force: true);

    private async Task RefreshAsync(bool force)
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        RefreshButton.IsEnabled = false;
        ErrorText.IsVisible = false;
        try { await _service.RefreshAllAsync(force); }
        catch (Exception ex) { ErrorText.Text = $"{L("rss.refresh_failed", "Refresh failed")}: {ex.Message}"; ErrorText.IsVisible = true; }
        finally { _isRefreshing = false; RefreshButton.IsEnabled = true; Reload(); }
    }

    private void OnMarkAllRead(object? sender, RoutedEventArgs e)
    {
        var settings = LoadSettings();
        _service.MarkAllRead(string.IsNullOrWhiteSpace(settings.RssReaderSourceId) ? null : settings.RssReaderSourceId);
    }

    private void OnOpen(object? sender, RoutedEventArgs e) => OpenReader(null);

    private void OnServiceChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Reload);

    private void Reload()
    {
        var settings = LoadSettings();
        var sourceId = string.IsNullOrWhiteSpace(settings.RssReaderSourceId) ? null : settings.RssReaderSourceId;
        var entries = _service.GetEntries(sourceId, limit: Math.Clamp(settings.RssReaderDisplayCount, 5, 100));
        if (settings.RssReaderUnreadFirst)
            entries = entries.OrderBy(entry => entry.IsRead).ThenByDescending(entry => entry.PublishedAt).ToArray();
        EntriesPanel.Children.Clear();
        foreach (var entry in entries)
        {
            var button = new Button
            {
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8),
                Tag = entry.Id,
                Content = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto"),
                    Children =
                    {
                        new TextBlock { Text = (entry.IsRead ? string.Empty : "● ") + entry.Title, FontWeight = entry.IsRead ? FontWeight.Normal : FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2 },
                        new TextBlock { Text = $"{entry.SourceTitle} · {FormatRelative(entry.PublishedAt)}{(entry.IsFavorite ? " · ★" : string.Empty)}", FontSize = 11, Opacity = 0.6, Margin = new Thickness(0,4,0,0), [Grid.RowProperty] = 1 }
                    }
                }
            };
            button.Click += OnEntryClicked;
            EntriesPanel.Children.Add(button);
        }
        var unread = _service.GetEntries(sourceId, unreadOnly: true, limit: 500).Count;
        UnreadText.Text = string.Format(L("rss.unread_count", "{0} unread"), unread);
        EmptyButton.IsVisible = _service.GetSources().Count == 0;
        EntriesPanel.IsVisible = !EmptyButton.IsVisible;
    }

    private void OnEntryClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        _service.MarkRead(id);
        OpenReader(id);
    }

    private void OpenReader(string? entryId) => AirAppLauncherServiceProvider.GetOrCreate()
        .OpenRssReader(BuiltInComponentIds.DesktopRssReader, _placementId, entryId);

    private ComponentSettingsSnapshot LoadSettings() =>
        _settingsAccessor.LoadSnapshot<ComponentSettingsSnapshot>() ?? new ComponentSettingsSnapshot();

    private string FormatRelative(DateTimeOffset value)
    {
        var elapsed = DateTimeOffset.Now - value.ToLocalTime();
        if (elapsed.TotalMinutes < 1) return L("rss.just_now", "Just now");
        if (elapsed.TotalHours < 1)
            return string.Format(L("rss.minutes_ago", "{0} min ago"), (int)elapsed.TotalMinutes);
        if (elapsed.TotalDays < 1)
            return string.Format(L("rss.hours_ago", "{0} hr ago"), (int)elapsed.TotalHours);

        var culture = CultureInfo.GetCultureInfo(_languageCode);
        return value.ToLocalTime().ToString(L("rss.short_date_format", "MMM d"), culture);
    }

    private void ApplyLocalization()
    {
        TitleText.Text = L("component.rss_reader", "RSS Reader");
        LatestText.Text = L("rss.latest_articles", "Latest articles");
        RefreshButtonText.Text = L("rss.refresh", "Refresh");
        MarkAllReadButtonText.Text = L("rss.mark_all_read", "Mark all read");
        OpenReaderButtonText.Text = L("rss.open_reader", "Open reader");
        EmptyButton.Content = L("rss.empty", "Import OPML or add your first RSS source");
    }

    private string L(string key, string fallback) => _localization.GetString(_languageCode, key, fallback);

    private void DisposeResources()
    {
        _refreshTimer.Stop();
        _service.Changed -= OnServiceChanged;
        _service.Dispose();
    }
}
