using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.RssReader;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.AirAppHost;

public sealed partial class RssReaderAirAppView : UserControl, IDisposable
{
    private readonly AirAppLaunchOptions _options;
    private readonly RssReaderService _service = new();
    private readonly LocalizationService _localization = new();
    private readonly string _languageCode;
    private string? _sourceId;
    private bool _unreadOnly;
    private bool _favoritesOnly;
    private RssEntry? _selectedEntry;
    private string? _lastHandledTargetEntryId;
    private readonly DispatcherTimer _refreshTimer = new();
    private NativeWebView? _articleWebView;

    public RssReaderAirAppView() : this(AirAppLaunchOptions.Parse([])) { }

    public RssReaderAirAppView(AirAppLaunchOptions options)
    {
        _options = options;
        try
        {
            _languageCode = _localization.NormalizeLanguageCode(new AppSettingsService().Load().LanguageCode);
        }
        catch
        {
            _languageCode = "zh-CN";
        }
        InitializeComponent();
        ApplyLocalization();
        AddButton.Click += OnAdd;
        RefreshButton.Click += OnRefresh;
        AllButton.Click += (_, _) => SetFilter(null, false, false, L("rss.all_articles", "All articles"));
        UnreadButton.Click += (_, _) => SetFilter(null, true, false, L("rss.unread", "Unread"));
        FavoritesButton.Click += (_, _) => SetFilter(null, false, true, L("rss.favorites", "Favorites"));
        SourcesList.SelectionChanged += OnSourceSelected;
        EditSourceButton.Click += OnEditSource;
        DeleteSourceButton.Click += OnDeleteSource;
        EntriesList.SelectionChanged += OnEntrySelected;
        MarkAllReadButton.Click += OnMarkAllRead;
        FavoriteButton.Click += OnFavorite;
        OpenOriginalButton.Click += OnOpenOriginal;
        ImportButton.Click += OnImport;
        ExportButton.Click += OnExport;
        SettingsButton.Click += OnSettings;
        _service.Changed += OnChanged;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(force: false);
        DetachedFromVisualTree += (_, _) => Dispose();
        Reload();
        ConfigureRefreshTimer();
        _ = RefreshAsync(force: true);
    }

    public void Dispose()
    {
        _service.Changed -= OnChanged;
        _refreshTimer.Stop();
        if (_articleWebView is not null)
        {
            ArticleHtmlHost.Children.Remove(_articleWebView);
            _articleWebView = null;
        }
        _service.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Reload);

    private void Reload()
    {
        var selectedSource = _sourceId;
        SourcesList.ItemsSource = _service.GetSources().Select(source => new SourceItem(source)).ToArray();
        var entries = _service.GetEntries(_sourceId, _unreadOnly, _favoritesOnly, 500);
        EntriesList.ItemsSource = entries.Select(entry => new EntryItem(entry)).ToArray();
        var targetEntryId = _options.TargetEntryId ?? _service.GetPendingEntryId();
        if (!string.IsNullOrWhiteSpace(targetEntryId) && targetEntryId != _lastHandledTargetEntryId)
        {
            var target = entries.FirstOrDefault(entry => entry.Id == targetEntryId) ?? _service.GetEntry(targetEntryId);
            if (target is not null) { _lastHandledTargetEntryId = targetEntryId; ShowEntry(target); }
        }
        StatusText.Text = string.Format(L("rss.status_counts", "{0} articles · {1} sources"), entries.Count, _service.GetSources().Count);
    }

    private void SetFilter(string? sourceId, bool unread, bool favorites, string title)
    {
        _sourceId = sourceId; _unreadOnly = unread; _favoritesOnly = favorites; ListTitle.Text = title; Reload();
    }

    private void OnSourceSelected(object? sender, SelectionChangedEventArgs e)
    {
        var hasSource = SourcesList.SelectedItem is SourceItem;
        EditSourceButton.IsEnabled = hasSource;
        DeleteSourceButton.IsEnabled = hasSource;
        if (SourcesList.SelectedItem is SourceItem item) SetFilter(item.Source.Id, false, false, item.Source.Title);
    }

    private async void OnEditSource(object? sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is not SourceItem item) return;
        var owner = TopLevel.GetTopLevel(this) as Window; if (owner is null) return;
        var title = new TextBox { Text = item.Source.Title, Width = 390 };
        var folder = new TextBox { Text = item.Source.Folder, Width = 390 };
        var enabled = new CheckBox { Content = L("rss.enabled", "Enabled"), IsChecked = item.Source.IsEnabled };
        var interval = new NumericUpDown { Minimum = 15, Maximum = 1440, Value = item.Source.RefreshIntervalMinutes ?? 30 };
        var save = new Button { Content = L("rss.save", "Save"), HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window { Title = L("rss.edit_source", "Edit RSS source"), Width = 460, Height = 360, CanResize = false };
        save.Click += (_, _) =>
        {
            _service.UpdateSource(item.Source.Id, title.Text ?? item.Source.Title, folder.Text, enabled.IsChecked == true, (int)(interval.Value ?? 30));
            dialog.Close();
        };
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 10, Children = { new TextBlock { Text = L("rss.name", "Name") }, title, new TextBlock { Text = L("rss.folder", "Folder") }, folder, enabled, new TextBlock { Text = L("rss.refresh_interval_minutes", "Refresh interval (minutes)") }, interval, save } };
        await dialog.ShowDialog(owner);
    }

    private async void OnDeleteSource(object? sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is not SourceItem item) return;
        var owner = TopLevel.GetTopLevel(this) as Window; if (owner is null) return;
        var preserve = new CheckBox { Content = L("rss.keep_favorites", "Keep favorited articles"), IsChecked = true };
        var delete = new Button { Content = L("rss.delete", "Delete"), HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = L("rss.cancel", "Cancel") };
        var dialog = new Window { Title = L("rss.delete_source", "Delete RSS source"), Width = 420, Height = 220, CanResize = false };
        delete.Click += (_, _) => { _service.DeleteSource(item.Source.Id, preserve.IsChecked == true); _sourceId = null; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = string.Format(L("rss.delete_source_confirm", "Delete {0}?"), item.Source.Title), FontSize = 18, TextWrapping = TextWrapping.Wrap }, preserve, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, delete } } } };
        await dialog.ShowDialog(owner);
    }

    private void OnEntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (EntriesList.SelectedItem is EntryItem item) ShowEntry(item.Entry);
    }

    private void ShowEntry(RssEntry entry)
    {
        if (!entry.IsRead) _service.MarkRead(entry.Id);
        _selectedEntry = entry with { IsRead = true };
        ArticleTitle.Text = entry.Title;
        ArticleMeta.Text = $"{entry.SourceTitle} · {entry.PublishedAt.ToLocalTime():g}{(string.IsNullOrWhiteSpace(entry.Author) ? string.Empty : " · " + entry.Author)}";
        var html = string.IsNullOrWhiteSpace(entry.Content) ? entry.Summary : entry.Content;
        ShowArticleContent(html);
        FavoriteButton.IsEnabled = true;
        FavoriteButton.Content = entry.IsFavorite ? $"★ {L("rss.favorited", "Favorited")}" : $"☆ {L("rss.favorite", "Favorite")}";
        OpenOriginalButton.IsEnabled = !string.IsNullOrWhiteSpace(entry.Link);
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e) => await RefreshAsync(force: true);
    private async Task RefreshAsync(bool force)
    {
        RefreshButton.IsEnabled = false; StatusText.Text = L("rss.refreshing", "Refreshing…");
        try { await _service.RefreshAllAsync(force); StatusText.Text = L("rss.updated", "Updated"); }
        catch (Exception ex) { StatusText.Text = $"{L("rss.refresh_failed", "Refresh failed")}: {ex.Message}"; }
        finally { RefreshButton.IsEnabled = true; Reload(); }
    }

    private void OnMarkAllRead(object? sender, RoutedEventArgs e) => _service.MarkAllRead(_sourceId);
    private void OnFavorite(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null) return;
        var favorite = !_selectedEntry.IsFavorite;
        _service.SetFavorite(_selectedEntry.Id, favorite);
        ShowEntry(_selectedEntry with { IsFavorite = favorite });
    }

    private void OnOpenOriginal(object? sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_selectedEntry?.Link, UriKind.Absolute, out var uri)) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void ShowArticleContent(string html)
    {
        var settings = _service.GetSettings();
        var sanitized = RssReaderService.SanitizeHtml(html, settings.LoadRemoteImages);
        ArticleBody.Text = RssReaderService.ToPlainText(sanitized);
        try
        {
            var availability = WebView2RuntimeProbe.GetAvailability();
            if (!availability.IsAvailable || string.IsNullOrWhiteSpace(sanitized))
            {
                ArticleHtmlHost.IsVisible = false;
                ArticleTextScroller.IsVisible = true;
                return;
            }

            _articleWebView ??= CreateArticleWebView();
            var imagePolicy = settings.LoadRemoteImages ? "https: http: data:" : "data:";
            var document = $$"""
                <!doctype html><html><head><meta charset="utf-8">
                <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src {{imagePolicy}}; style-src 'unsafe-inline'">
                <style>body{font-family:system-ui,sans-serif;margin:24px;color:#202124;line-height:1.65;font-size:16px}img{max-width:100%;height:auto}a{color:#0067c0}pre{white-space:pre-wrap}</style>
                </head><body>{{sanitized}}</body></html>
                """;
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(document));
            _articleWebView.Navigate(new Uri($"data:text/html;base64,{encoded}"));
            ArticleTextScroller.IsVisible = false;
            ArticleHtmlHost.IsVisible = true;
        }
        catch
        {
            ArticleHtmlHost.IsVisible = false;
            ArticleTextScroller.IsVisible = true;
        }
    }

    private NativeWebView CreateArticleWebView()
    {
        var webView = new NativeWebView();
        ArticleHtmlHost.Children.Add(webView);
        return webView;
    }

    private async void OnAdd(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;
        var url = new TextBox { PlaceholderText = "https://example.com/feed.xml", Width = 430 };
        var title = new TextBox { PlaceholderText = L("rss.optional_name", "Optional custom name"), Width = 430 };
        var folder = new TextBox { PlaceholderText = L("rss.optional_folder", "Optional folder"), Width = 430 };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var add = new Button { Content = L("rss.probe_add", "Probe and add"), HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window { Title = L("rss.add_source", "Add RSS source"), Width = 500, Height = 330, CanResize = false };
        add.Click += async (_, _) =>
        {
            add.IsEnabled = false; message.Text = L("rss.checking_feed", "Checking feed…");
            try
            {
                var probe = await _service.ProbeAsync(url.Text ?? string.Empty);
                message.Text = $"{probe.Title} · {probe.Format}";
                await _service.AddSourceAsync(url.Text ?? string.Empty, title.Text, folder.Text);
                dialog.Close();
            }
            catch (Exception ex) { message.Text = ex.Message; add.IsEnabled = true; }
        };
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children = { new TextBlock { Text = L("rss.feed_url", "Feed URL") }, url, new TextBlock { Text = L("rss.name", "Name") }, title, new TextBlock { Text = L("rss.folder", "Folder") }, folder, message, add } };
        await dialog.ShowDialog(owner);
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L("rss.import_opml", "Import OPML"), AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("OPML") { Patterns = ["*.opml", "*.xml"] }] });
        var path = files.FirstOrDefault()?.TryGetLocalPath(); if (path is null) return;
        var result = await _service.ImportOpmlAsync(path); StatusText.Text = string.Format(L("rss.import_result", "Imported {0}, skipped {1}, failed {2}."), result.Added, result.Skipped, result.Failed);
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = L("rss.export_opml", "Export OPML"), SuggestedFileName = "subscriptions.opml", FileTypeChoices = [new FilePickerFileType("OPML") { Patterns = ["*.opml"] }] });
        var path = file?.TryGetLocalPath(); if (path is null) return; _service.ExportOpml(path); StatusText.Text = L("rss.exported", "OPML exported.");
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window; if (owner is null) return;
        var settings = _service.GetSettings();
        var interval = new ComboBox { ItemsSource = new[] { new Choice(15, string.Format(L("rss.minutes", "{0} minutes"), 15)), new Choice(30, string.Format(L("rss.minutes", "{0} minutes"), 30)), new Choice(60, string.Format(L("rss.minutes", "{0} minutes"), 60)), new Choice(0,L("rss.manual_only", "Manual only")) }, SelectedIndex = settings.RefreshIntervalMinutes switch { 15 => 0, 60 => 2, 0 => 3, _ => 1 } };
        var images = new CheckBox { Content = L("rss.load_remote_images", "Load remote images"), IsChecked = settings.LoadRemoteImages };
        var save = new Button { Content = L("rss.save", "Save"), HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window { Title = L("rss.settings", "RSS settings"), Width = 420, Height = 240, CanResize = false };
        save.Click += (_, _) => { var value = (interval.SelectedItem as Choice)?.Value ?? 30; _service.SaveSettings(settings with { RefreshIntervalMinutes = value, LoadRemoteImages = images.IsChecked == true }); ConfigureRefreshTimer(); dialog.Close(); };
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14, Children = { new TextBlock { Text = L("rss.refresh_interval", "Refresh interval") }, interval, images, save } };
        await dialog.ShowDialog(owner);
    }

    private sealed record SourceItem(RssSource Source) { public override string ToString() => string.IsNullOrWhiteSpace(Source.Folder) ? Source.Title : $"{Source.Folder} / {Source.Title}"; }

    private void ConfigureRefreshTimer()
    {
        var minutes = _service.GetSettings().RefreshIntervalMinutes;
        _refreshTimer.Stop();
        if (minutes <= 0) return;
        _refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
        _refreshTimer.Start();
    }

    private void ApplyLocalization()
    {
        AddButton.Content = L("rss.add_source", "Add source");
        RefreshButton.Content = L("rss.refresh", "Refresh");
        AllButton.Content = L("rss.all_articles", "All articles");
        UnreadButton.Content = L("rss.unread", "Unread");
        FavoritesButton.Content = L("rss.favorites", "Favorites");
        EditSourceButton.Content = L("rss.edit", "Edit");
        DeleteSourceButton.Content = L("rss.delete", "Delete");
        ImportButton.Content = L("rss.import_opml", "Import OPML");
        ExportButton.Content = L("rss.export_opml", "Export OPML");
        SettingsButton.Content = L("rss.settings", "Settings");
        ListTitle.Text = L("rss.all_articles", "All articles");
        MarkAllReadButton.Content = L("rss.mark_all_read", "Mark all read");
        ArticleTitle.Text = L("rss.select_article", "Select an article");
        ArticleBody.Text = L("rss.article_placeholder", "Your RSS articles will appear here.");
        FavoriteButton.Content = $"☆ {L("rss.favorite", "Favorite")}";
        OpenOriginalButton.Content = L("rss.open_original", "Open original");
    }

    private string L(string key, string fallback) => _localization.GetString(_languageCode, key, fallback);
    private sealed record EntryItem(RssEntry Entry)
    {
        public string DisplayTitle => (Entry.IsRead ? string.Empty : "● ") + Entry.Title;
        public FontWeight Weight => Entry.IsRead ? FontWeight.Normal : FontWeight.SemiBold;
        public string Metadata => $"{Entry.SourceTitle} · {Entry.PublishedAt.ToLocalTime():g}{(Entry.IsFavorite ? " · ★" : string.Empty)}";
    }
    private sealed record Choice(int Value, string Label) { public override string ToString() => Label; }
}
