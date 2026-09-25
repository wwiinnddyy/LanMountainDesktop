using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Helpers;

namespace LanMountainDesktop.Views.Components;

public partial class CnrDailyNewsWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget, ISettingsAwareComponentWidget
{
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();
    private static readonly HttpClient ImageHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };


    private static readonly IReadOnlyList<int> SupportedAutoRotateIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(30)
    };

    private readonly bool _isDesignModePreview = Design.IsDesignMode;
    private LanMountainDesktop.AirAppSdk.ISettingsService _appSettingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsService = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly Bitmap?[] _newsBitmaps = new Bitmap?[2];
    private readonly List<string?> _newsUrls = [];
    private IReadOnlyList<DailyNewsItemSnapshot> _activeNewsItems = [];

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private readonly ComponentFeedRefresh _feed = new();
    private string _languageCode = LocalizationService.DefaultLanguageCode;
    private bool _isAttached;

    public CnrDailyNewsWidget()
    {
        InitializeComponent();

        if (_isDesignModePreview)
        {
            ApplyDesignTimePreview();
            return;
        }

        _refreshTimer.Tick += OnRefreshTimerTick;
        RefreshButton.Click += OnRefreshButtonClick;
        NewsItem1Card.PointerPressed += OnNewsItem1PointerPressed;
        NewsItem2Card.PointerPressed += OnNewsItem2PointerPressed;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        UpdateLanguageCode();
        ApplyAutoRotateSettings();
        ApplyLoadingState();
        UpdateRefreshButtonState();
    }

    public void ApplyCellSize(double cellSize)
    {
        _ = cellSize;
    }

    public void SetRecommendationInfoService(IRecommendationInfoService recommendationInfoService)
    {
        RecommendationServiceBinding.Attach(
            ref _recommendationService,
            recommendationInfoService,
            DefaultRecommendationService,
            () => _isAttached,
            () => RefreshNewsAsync(forceRefresh: false));
    }

    public void RefreshFromSettings()
    {
        RecommendationServiceBinding.AfterSettingsChange(
            _recommendationService.ClearCache,
            ApplyAutoRotateSettings,
            () => _isAttached,
            () => RefreshNewsAsync(forceRefresh: true));
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ComponentRefreshLifetime.Attach(
            ref _isAttached, ApplyAutoRotateSettings, UpdateRefreshButtonState,
            () => RefreshNewsAsync(forceRefresh: false));
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ComponentRefreshLifetime.Detach(ref _isAttached, _refreshTimer, ref _feed.InFlight);
        DisposeNewsBitmaps();
        UpdateRefreshButtonState();
    }

    private async void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isDesignModePreview)
        {
            e.Handled = true;
            return;
        }

        if (_feed.IsBusy)
        {
            return;
        }

        await RefreshNewsAsync(forceRefresh: true);
        e.Handled = true;
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshNewsAsync(forceRefresh: true);
    }

    private void OnNewsItem1PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDesignModePreview)
        {
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TryOpenNewsUrl(0);
        e.Handled = true;
    }

    private void OnNewsItem2PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDesignModePreview)
        {
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TryOpenNewsUrl(1);
        e.Handled = true;
    }

    private async Task RefreshNewsAsync(bool forceRefresh) =>
        await _feed.RunAsync(
            () => _isAttached,
            () =>
            {
                UpdateRefreshButtonState();
                UpdateLanguageCode();
            },
            async token =>
            {
                var query = new DailyNewsQuery(
                    Locale: _languageCode,
                    ItemCount: ResolveDesiredNewsItemCount(),
                    ForceRefresh: forceRefresh);
                var result = await _recommendationService.GetDailyNewsAsync(query, token);
                if (!result.Success || result.Data is null)
                {
                    return false;
                }

                await ApplySnapshotAsync(result.Data, token);
                return true;
            },
            ApplyFailedState,
            UpdateRefreshButtonState);

    private async Task ApplySnapshotAsync(DailyNewsSnapshot snapshot, CancellationToken cancellationToken)
    {
        var items = snapshot.Items is null
            ? []
            : snapshot.Items.Take(2).ToArray();
        _activeNewsItems = items;

        var item1 = items.Length > 0 ? items[0] : null;
        var item2 = items.Length > 1 ? items[1] : null;

        UpdateHotHeadlineText(item1?.Title);
        News2TitleTextBlock.Text = CompactText.Normalize(item2?.Title);

        _newsUrls.Clear();
        foreach (var item in items)
        {
            _newsUrls.Add(ExternalLinkLauncher.NormalizeHttpUrl(item.Url));
        }

        UpdateNewsInteractionState();

        StatusTextBlock.IsVisible = false;

        var loadTasks = new[]
        {
            RemoteImageBitmap.GetAsync(ImageHttpClient, item1?.ImageUrl, cancellationToken),
            RemoteImageBitmap.GetAsync(ImageHttpClient, item2?.ImageUrl, cancellationToken)
        };
        var bitmaps = await Task.WhenAll(loadTasks);
        if (cancellationToken.IsCancellationRequested || !_isAttached)
        {
            bitmaps[0]?.Dispose();
            bitmaps[1]?.Dispose();
            return;
        }

        SetNewsBitmap(0, bitmaps[0]);
        SetNewsBitmap(1, bitmaps[1]);
    }

    private void ApplyLoadingState()
    {
        _activeNewsItems = [];
        _newsUrls.Clear();
        UpdateHotHeadlineText(L("cnrnews.widget.loading_title", "Loading headlines"));
        News2TitleTextBlock.Text = L("cnrnews.widget.loading_subtitle", "Please wait");
        StatusTextBlock.Text = L("cnrnews.widget.loading", "Loading...");
        StatusTextBlock.IsVisible = true;
        SetNewsBitmap(0, null);
        SetNewsBitmap(1, null);
        UpdateNewsInteractionState();
    }

    private void ApplyFailedState()
    {
        _activeNewsItems = [];
        _newsUrls.Clear();
        News1TitleTextBlock.Inlines = null;
        News1TitleTextBlock.Text = L("cnrnews.widget.fallback_title", "CNR news is temporarily unavailable");
        News2TitleTextBlock.Text = L("cnrnews.widget.fallback_subtitle", "Tap refresh and try again");
        StatusTextBlock.Text = L("cnrnews.widget.fetch_failed", "News fetch failed");
        StatusTextBlock.IsVisible = true;
        SetNewsBitmap(0, null);
        SetNewsBitmap(1, null);
        UpdateNewsInteractionState();
    }

    private void ApplyDesignTimePreview()
    {
        _activeNewsItems =
        [
            new DailyNewsItemSnapshot(
                "LanMountain preview mode now shows mocked widget content in Rider.",
                null,
                "https://example.com/news/preview-1",
                null,
                "09:30"),
            new DailyNewsItemSnapshot(
                "Weather, artwork, and AirApp market cards render without live network calls.",
                null,
                "https://example.com/news/preview-2",
                null,
                "09:10")
        ];

        _newsUrls.Clear();
        foreach (var item in _activeNewsItems)
        {
            _newsUrls.Add(item.Url);
        }

        UpdateHotHeadlineText(_activeNewsItems[0].Title);
        News2TitleTextBlock.Text = CompactText.Normalize(_activeNewsItems[1].Title);
        StatusTextBlock.Text = string.Empty;
        StatusTextBlock.IsVisible = false;

        SetNewsBitmap(0, null);
        SetNewsBitmap(1, null);
        UpdateNewsInteractionState();

        RefreshButton.IsEnabled = false;
        RefreshButton.Opacity = 1.0;
    }

    private int ResolveDesiredNewsItemCount()
    {
        return 2;
    }

    private void UpdateHotHeadlineText(string? title)
    {
        var normalizedTitle = CompactText.Normalize(title);
        var hotLabel = L("cnrnews.widget.hot_label", "Hot");

        if (News1TitleTextBlock.Inlines is null)
        {
            News1TitleTextBlock.Text = $"{hotLabel} | {normalizedTitle}";
            return;
        }

        News1TitleTextBlock.Inlines.Clear();
        News1TitleTextBlock.Inlines.Add(new Run($"{hotLabel} | ")
        {
            Foreground = new SolidColorBrush(Color.Parse("#D6272E")),
            FontWeight = FontWeight.SemiBold
        });
        News1TitleTextBlock.Inlines.Add(new Run(normalizedTitle)
        {
            FontWeight = FontWeight.SemiBold
        });
    }

    // 淡出故意只看"没上台面"、不跟禁用同判据：这是收口前的既有形状，保留并写明分叉（见家的注释）。
    private void UpdateRefreshButtonState() =>
        ComponentBusyVisual.Apply(RefreshButton, !_feed.IsBusy && _isAttached, !_isAttached, dimmedOpacity: 0.6);

    private void UpdateNewsInteractionState()
    {
        var cards = new[] { NewsItem1Card, NewsItem2Card };
        for (var i = 0; i < cards.Length; i++)
        {
            var enabled = i < _newsUrls.Count && !string.IsNullOrWhiteSpace(_newsUrls[i]);
            cards[i].IsHitTestVisible = enabled;
            cards[i].Opacity = enabled ? 1.0 : 0.72;
        }
    }

    private void TryOpenNewsUrl(int index)
    {
        if (index < 0 || index >= _newsUrls.Count)
        {
            return;
        }

        ExternalLinkLauncher.TryOpen(_newsUrls[index]);
    }

    private void SetNewsBitmap(int index, Bitmap? bitmap)
    {
        if (index < 0 || index >= _newsBitmaps.Length)
        {
            bitmap?.Dispose();
            return;
        }

        var imageControl = index switch
        {
            0 => News1Image,
            1 => News2Image,
            _ => null
        };

        if (imageControl is null)
        {
            bitmap?.Dispose();
            return;
        }

        var oldBitmap = _newsBitmaps[index];
        if (ReferenceEquals(imageControl.Source, oldBitmap))
        {
            imageControl.Source = null;
        }

        oldBitmap?.Dispose();
        _newsBitmaps[index] = bitmap;
        imageControl.Source = bitmap;

        if (bitmap != null)
        {
            InvalidateMeasure();
        }
    }

    private void DisposeNewsBitmaps()
    {
        for (var i = 0; i < _newsBitmaps.Length; i++)
        {
            SetNewsBitmap(i, null);
        }
    }

    private void UpdateLanguageCode() =>
        _languageCode = _localizationService.ResolveLanguageCode(() => _appSettingsService.Load().LanguageCode);

    private void ApplyAutoRotateSettings()
    {
        var enabled = true;
        var intervalMinutes = 60;

        try
        {
            var snapshot = _componentSettingsService.Load();
            enabled = snapshot.CnrDailyNewsAutoRotateEnabled;
            intervalMinutes = NormalizeAutoRotateIntervalMinutes(snapshot.CnrDailyNewsAutoRotateIntervalMinutes);
        }
        catch
        {
            // Keep fallback defaults.
        }

        ComponentRefreshLifetime.Reschedule(_refreshTimer, _isAttached, enabled, intervalMinutes);
    }

    private static int NormalizeAutoRotateIntervalMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return 60;
        }

        if (SupportedAutoRotateIntervalsMinutes.Contains(minutes))
        {
            return minutes;
        }

        return SupportedAutoRotateIntervalsMinutes
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(60);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
