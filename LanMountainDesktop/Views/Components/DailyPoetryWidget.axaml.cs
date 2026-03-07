using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class DailyPoetryWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget
{
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly char[] NaturalBreakChars =
    [
        '\uFF0C',
        '\u3002',
        '\uFF01',
        '\uFF1F',
        '\uFF1B',
        '\u3001',
        '\uFF1A',
        ',',
        '.',
        '!',
        '?',
        ';',
        ':',
        '-',
        '\u00B7'
    ];
    private static readonly HashSet<char> NaturalBreakCharSet = new(NaturalBreakChars);

    private static readonly FontFamily MiSansFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();

    private const double BaseCellSize = 48d;
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 2;
    private const double MinPoetryFontSize = 8;
    private const double MinAuthorFontSize = 7;

    private readonly record struct TextFitResult(double FontSize, FontWeight FontWeight, double LineHeight);

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };

    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();

    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private CancellationTokenSource? _refreshCts;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = 48;
    private bool _isAttached;
    private bool _isRefreshing;
    private bool? _isNightModeApplied;
    private string _poetryRawText = string.Empty;
    private string _authorRawText = string.Empty;

    public DailyPoetryWidget()
    {
        InitializeComponent();

        PoetryContentTextBlock.FontFamily = MiSansFontFamily;
        AuthorTextBlock.FontFamily = MiSansFontFamily;

        _refreshTimer.Tick += OnRefreshTimerTick;
        RefreshButton.Click += OnRefreshButtonClick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        UpdateLanguageCode();
        ApplyLoadingState();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 16, 52));
        RootBorder.Padding = new Thickness(
            Math.Clamp(20 * scale, 10, 34),
            Math.Clamp(16 * scale, 8, 28),
            Math.Clamp(20 * scale, 10, 34),
            Math.Clamp(14 * scale, 7, 24));

        QuoteMarkTextBlock.FontSize = Math.Clamp(80 * scale, 32, 120);
        QuoteMarkTextBlock.LineHeight = Math.Clamp(68 * scale, 26, 100);
        QuoteMarkTextBlock.Margin = new Thickness(Math.Clamp(1 * scale, 0, 3), 0, 0, 0);

        PoetryContentTextBlock.Margin = new Thickness(
            Math.Clamp(8 * scale, 4, 16),
            Math.Clamp(2 * scale, 0, 8),
            0,
            0);

        AuthorAccent.Width = Math.Clamp(6 * scale, 3.2, 9.5);
        AuthorAccent.Height = Math.Clamp(24 * scale, 12, 34);
        AuthorAccent.Margin = new Thickness(0, 0, Math.Clamp(8 * scale, 4, 13), 0);
        AuthorAccent.CornerRadius = new CornerRadius(Math.Clamp(3 * scale, 1.5, 4.5));

        StatusTextBlock.FontSize = Math.Clamp(17 * scale, 9, 26);

        DayDecorationCanvas.Width = Math.Clamp(170 * scale, 88, 248);
        DayDecorationCanvas.Height = Math.Clamp(118 * scale, 62, 174);
        DayDecorationCanvas.Margin = new Thickness(
            0,
            Math.Clamp(36 * scale, 16, 56),
            Math.Clamp(16 * scale, 8, 24),
            0);

        var refreshTouchSize = Math.Clamp(42 * scale, 24, 52);
        RefreshButton.Width = refreshTouchSize;
        RefreshButton.Height = refreshTouchSize;
        RefreshButton.CornerRadius = new CornerRadius(refreshTouchSize / 2d);
        RefreshButton.Margin = new Thickness(
            0,
            Math.Clamp(12 * scale, 4, 20),
            Math.Clamp(16 * scale, 6, 24),
            0);

        RefreshGlyphTextBlock.FontSize = Math.Clamp(26 * scale, 14, 34);
        RefreshGlyphTextBlock.LineHeight = RefreshGlyphTextBlock.FontSize;

        WavePath.StrokeThickness = Math.Clamp(3.0 * scale, 1.2, 4.2);

        ApplyModeVisualIfNeeded(force: true);
    }

    public void SetRecommendationInfoService(IRecommendationInfoService recommendationInfoService)
    {
        _recommendationService = recommendationInfoService ?? DefaultRecommendationService;
        if (_isAttached)
        {
            _ = RefreshPoetryAsync(forceRefresh: false);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        UpdateRefreshButtonState();
        ApplyModeVisualIfNeeded();
        _refreshTimer.Start();
        _ = RefreshPoetryAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
        UpdateRefreshButtonState();
    }

    private async void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        await RefreshPoetryAsync(forceRefresh: true);
        e.Handled = true;
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ApplyModeVisualIfNeeded(force: true);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshPoetryAsync(forceRefresh: false);
    }

    private async Task RefreshPoetryAsync(bool forceRefresh)
    {
        if (!_isAttached || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdateRefreshButtonState();
        UpdateLanguageCode();

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var query = new DailyPoetryQuery(
                Locale: _languageCode,
                ForceRefresh: forceRefresh);

            var result = await _recommendationService.GetDailyPoetryAsync(query, cts.Token);
            if (!_isAttached || cts.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                ApplyFailedState();
                return;
            }

            ApplySnapshot(result.Data);
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled requests.
        }
        catch
        {
            if (_isAttached && !cts.IsCancellationRequested)
            {
                ApplyFailedState();
            }
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, cts))
            {
                _refreshCts = null;
            }

            cts.Dispose();
            _isRefreshing = false;
            UpdateRefreshButtonState();
        }
    }

    private void ApplySnapshot(DailyPoetrySnapshot snapshot)
    {
        _poetryRawText = NormalizePoetryContent(snapshot.Content);
        _authorRawText = ResolveAuthor(snapshot);
        StatusTextBlock.IsVisible = false;
        ApplyModeVisualIfNeeded(force: true);
    }

    private string ResolveAuthor(DailyPoetrySnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Author))
        {
            if (!string.IsNullOrWhiteSpace(snapshot.Origin))
            {
                return $"{snapshot.Origin.Trim()} \u00B7 {snapshot.Author.Trim()}";
            }

            return snapshot.Author.Trim();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Origin))
        {
            return snapshot.Origin.Trim();
        }

        return L("poetry.widget.unknown_author", "Unknown");
    }

    private static string NormalizePoetryContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return content
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private void ApplyLoadingState()
    {
        _poetryRawText = L("poetry.widget.loading_content", "Loading...");
        _authorRawText = L("poetry.widget.loading_author", "...");
        StatusTextBlock.IsVisible = false;
        ApplyModeVisualIfNeeded(force: true);
    }

    private void ApplyFailedState()
    {
        _poetryRawText = L("poetry.widget.fallback_content", "Poetry is temporarily unavailable.");
        _authorRawText = L("poetry.widget.fallback_author", "Try again later");
        StatusTextBlock.Text = L("poetry.widget.fetch_failed", "Poetry fetch failed");
        StatusTextBlock.IsVisible = true;
        ApplyModeVisualIfNeeded(force: true);
    }

    private void ApplyModeVisualIfNeeded(bool force = false)
    {
        var isNightMode = ResolveIsNightMode();
        if (!force && _isNightModeApplied.HasValue && _isNightModeApplied.Value == isNightMode)
        {
            return;
        }

        _isNightModeApplied = isNightMode;
        ApplyModeVisual(isNightMode);
    }

    private void ApplyModeVisual(bool isNightMode)
    {
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;
        var scale = ResolveScale();

        if (isNightMode)
        {
            RootBorder.Background = CreateBrush("#C5070D");
            RootBorder.Padding = new Thickness(
                Math.Clamp(20 * scale, 10, 34),
                Math.Clamp(15 * scale, 7, 24),
                Math.Clamp(20 * scale, 10, 34),
                Math.Clamp(14 * scale, 7, 24));

            QuoteMarkTextBlock.IsVisible = true;
            QuoteMarkTextBlock.Foreground = CreateBrush("#4AF4C5A6");
            QuoteMarkTextBlock.FontWeight = ToVariableWeight(610);

            PoetryContentTextBlock.Foreground = CreateBrush("#F4D7A7");
            PoetryContentTextBlock.VerticalAlignment = totalHeight >= _currentCellSize * 1.88
                ? Avalonia.Layout.VerticalAlignment.Center
                : Avalonia.Layout.VerticalAlignment.Top;
            PoetryContentTextBlock.Margin = new Thickness(Math.Clamp(10 * scale, 4, 18), Math.Clamp(2 * scale, 0, 6), 0, 0);

            AuthorTextBlock.Foreground = CreateBrush("#F4D7A7");
            AuthorAccent.Background = CreateBrush("#63F2AF90");

            DayDecorationCanvas.IsVisible = false;
            RefreshButton.IsVisible = true;
            RefreshButton.Background = CreateBrush("#24F8D7B2");
            RefreshGlyphTextBlock.Foreground = CreateBrush("#EED7B2");
            StatusTextBlock.Foreground = CreateBrush("#D9FFFFFF");
        }
        else
        {
            RootBorder.Background = CreateBrush("#F2F2F3");
            RootBorder.Padding = new Thickness(
                Math.Clamp(20 * scale, 10, 34),
                Math.Clamp(14 * scale, 6, 24),
                Math.Clamp(20 * scale, 10, 34),
                Math.Clamp(14 * scale, 7, 24));

            QuoteMarkTextBlock.IsVisible = false;

            PoetryContentTextBlock.Foreground = CreateBrush("#0F1218");
            PoetryContentTextBlock.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            PoetryContentTextBlock.Margin = new Thickness(Math.Clamp(6 * scale, 2, 12), 0, 0, 0);

            AuthorTextBlock.Foreground = CreateBrush("#272D38");
            AuthorAccent.Background = CreateBrush("#C8090D");

            DayDecorationCanvas.IsVisible = true;
            RefreshButton.IsVisible = true;
            RefreshButton.Background = CreateBrush("#0DA6ADB7");
            RefreshGlyphTextBlock.Foreground = CreateBrush("#90959D");
            WavePath.Stroke = CreateBrush("#B0B6BE");
            MountainBackPath.Fill = CreateBrush("#112A2E36");
            MountainFrontPath.Fill = CreateBrush("#182A2E36");
            StatusTextBlock.Foreground = CreateBrush("#8A8F98");
        }

        UpdateRefreshButtonState();
        ApplyAdaptiveTextLayout(isNightMode, scale, totalWidth, totalHeight);
    }

    private bool ResolveIsNightMode()
    {
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }

        if (ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }

        if (this.TryFindResource("AdaptiveSurfaceBaseBrush", out var value) &&
            value is ISolidColorBrush solidBrush)
        {
            return CalculateRelativeLuminance(solidBrush.Color) < 0.45;
        }

        return false;
    }

    private void UpdateLanguageCode()
    {
        try
        {
            var snapshot = _settingsService.Load();
            _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        }
        catch
        {
            _languageCode = "zh-CN";
        }
    }

    private void CancelRefreshRequest()
    {
        var cts = Interlocked.Exchange(ref _refreshCts, null);
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / BaseCellSize, 0.52, 2.2);
        var widthScale = Bounds.Width > 1
            ? Math.Clamp(Bounds.Width / Math.Max(1, _currentCellSize * BaseWidthCells), 0.52, 2.2)
            : 1;
        var heightScale = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height / Math.Max(1, _currentCellSize * BaseHeightCells), 0.52, 2.2)
            : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale)), 0.52, 2.2);
    }

    private void ApplyAdaptiveTextLayout(bool isNightMode, double scale, double totalWidth, double totalHeight)
    {
        var padding = RootBorder.Padding;
        var innerWidth = Math.Max(84, totalWidth - padding.Left - padding.Right);
        var innerHeight = Math.Max(56, totalHeight - padding.Top - padding.Bottom);

        var showDayDecorations = !isNightMode &&
                                 innerWidth >= Math.Max(_currentCellSize * 2.75, 146) &&
                                 innerHeight >= Math.Max(_currentCellSize * 1.02, 62);
        DayDecorationCanvas.IsVisible = showDayDecorations;
        RefreshButton.IsVisible = true;

        var refreshButtonWidth = 42 + Math.Clamp(8 * scale, 5, 14);
        var quoteMarkWidth = QuoteMarkTextBlock.IsVisible ? Math.Clamp(10 * scale, 5, 16) : 0;
        
        var poemWidth = innerWidth - quoteMarkWidth - Math.Clamp(12 * scale, 6, 20);
        poemWidth = Math.Min(Math.Max(64, poemWidth), innerWidth - Math.Clamp(16 * scale, 8, 24));

        var poemMaxLines = 2;
        var poemUnitsTarget = EstimateTargetUnitsPerLine(poemWidth, scale, isNightMode);
        var poemPrepared = PreparePoetryText(_poetryRawText, poemUnitsTarget, poemMaxLines);
        
        var availablePoemHeight = innerHeight * 0.72;
        var poemPreferredFontSize = Math.Clamp((isNightMode ? 34 : 32) * scale, 14, 56);
        var poemMinFontSize = Math.Clamp(poemPreferredFontSize * 0.65, MinPoetryFontSize, poemPreferredFontSize);
        var poemMinWeight = isNightMode ? 540 : 500;
        var poemMaxWeight = isNightMode ? 760 : 680;
        poemPrepared = EnsureTextFitsAtMinSize(
            preparedText: poemPrepared,
            sourceText: _poetryRawText,
            targetUnits: poemUnitsTarget,
            maxLines: poemMaxLines,
            maxWidth: poemWidth,
            maxHeight: availablePoemHeight,
            minFontSize: poemMinFontSize,
            minFontWeight: ToVariableWeight(poemMinWeight),
            lineHeightFactor: 1.12);

        var poemFit = FitTextStable(
            poemPrepared,
            poemWidth,
            availablePoemHeight,
            minFontSize: poemMinFontSize,
            maxFontSize: Math.Clamp(poemPreferredFontSize * 1.20, poemMinFontSize, 62),
            maxLines: poemMaxLines,
            lineHeightFactor: 1.12,
            minWeight: poemMinWeight,
            maxWeight: poemMaxWeight);

        PoetryContentTextBlock.Text = poemPrepared;
        PoetryContentTextBlock.MaxWidth = poemWidth;
        PoetryContentTextBlock.MaxLines = poemMaxLines;
        PoetryContentTextBlock.FontSize = poemFit.FontSize;
        PoetryContentTextBlock.LineHeight = poemFit.LineHeight;
        PoetryContentTextBlock.FontWeight = poemFit.FontWeight;

        var authorWidth = Math.Max(72, Math.Min(innerWidth * (isNightMode ? 0.5 : 0.56), innerWidth - 8));
        var authorUnitsTarget = 20;
        var authorPrepared = PrepareAuthorText(_authorRawText, authorUnitsTarget, 1);
        var authorPreferredFontSize = Math.Clamp((isNightMode ? 25 : 23) * scale, 10, 34);
        var authorMinFontSize = Math.Clamp(authorPreferredFontSize * 0.65, MinAuthorFontSize, authorPreferredFontSize);
        var authorMinWeight = isNightMode ? 500 : 470;
        var authorMaxWeight = isNightMode ? 650 : 600;
        authorPrepared = EnsureTextFitsAtMinSize(
            preparedText: authorPrepared,
            sourceText: _authorRawText,
            targetUnits: authorUnitsTarget,
            maxLines: 1,
            maxWidth: authorWidth,
            maxHeight: AuthorAccent.Height,
            minFontSize: authorMinFontSize,
            minFontWeight: ToVariableWeight(authorMinWeight),
            lineHeightFactor: 1.12);

        var authorFit = FitTextStable(
            authorPrepared,
            authorWidth,
            AuthorAccent.Height,
            minFontSize: authorMinFontSize,
            maxFontSize: Math.Clamp(authorPreferredFontSize * 1.15, authorMinFontSize, 42),
            maxLines: 1,
            lineHeightFactor: 1.12,
            minWeight: authorMinWeight,
            maxWeight: authorMaxWeight);

        AuthorTextBlock.Text = authorPrepared;
        AuthorTextBlock.TextWrapping = TextWrapping.NoWrap;
        AuthorTextBlock.MaxLines = 1;
        AuthorTextBlock.MaxWidth = authorWidth;
        AuthorTextBlock.FontSize = authorFit.FontSize;
        AuthorTextBlock.LineHeight = authorFit.LineHeight;
        AuthorTextBlock.FontWeight = authorFit.FontWeight;
    }

    private void UpdateRefreshButtonState()
    {
        RefreshButton.IsEnabled = !_isRefreshing;
        RefreshGlyphTextBlock.Opacity = _isRefreshing ? 0.56 : 1.0;
        RefreshButton.Opacity = _isAttached ? 1.0 : 0.85;
    }

    private static string PrepareAuthorText(string? rawText, int targetUnits, int maxLines)
    {
        var normalized = NormalizeCompactText(rawText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var separatorIndex = normalized.IndexOf(" \u00B7 ", StringComparison.Ordinal);
        if (separatorIndex > 0 && maxLines > 1)
        {
            normalized = string.Concat(
                normalized.AsSpan(0, separatorIndex),
                " ",
                normalized.AsSpan(separatorIndex + 3));
        }

        var wrapped = WrapByUnits(RemoveLineBreaks(normalized), targetUnits, maxLines);
        if (!string.IsNullOrWhiteSpace(wrapped))
        {
            return wrapped;
        }

        var compact = RemoveLineBreaks(normalized);
        var fallbackLength = Math.Max(2, Math.Min(compact.Length, Math.Max(4, targetUnits)));
        return compact[..fallbackLength];
    }

    private static string PreparePoetryText(string? rawText, int targetUnits, int maxLines)
    {
        var normalized = NormalizePoetryContent(rawText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var compact = RemoveLineBreaks(normalized);
        var wrapped = WrapByUnits(compact, targetUnits, maxLines);
        if (!string.IsNullOrWhiteSpace(wrapped))
        {
            return wrapped;
        }

        var fallbackLength = Math.Max(4, Math.Min(compact.Length, Math.Max(8, targetUnits * maxLines)));
        return compact[..fallbackLength];
    }

    private static string EnsureTextFitsAtMinSize(
        string preparedText,
        string? sourceText,
        int targetUnits,
        int maxLines,
        double maxWidth,
        double maxHeight,
        double minFontSize,
        FontWeight minFontWeight,
        double lineHeightFactor)
    {
        var compactPrepared = RemoveLineBreaks(preparedText);
        var compactSource = RemoveLineBreaks(sourceText);
        var effectiveSource = string.IsNullOrWhiteSpace(compactSource) ? compactPrepared : compactSource;
        if (string.IsNullOrWhiteSpace(effectiveSource))
        {
            return string.Empty;
        }

        var safeTargetUnits = Math.Max(4, targetUnits);
        var safeMaxLines = Math.Max(1, maxLines);
        var candidate = string.IsNullOrWhiteSpace(compactPrepared)
            ? WrapByUnits(effectiveSource, safeTargetUnits, safeMaxLines)
            : WrapByUnits(compactPrepared, safeTargetUnits, safeMaxLines);

        if (DoesTextFit(candidate, maxWidth, maxHeight, safeMaxLines, minFontSize, minFontWeight, lineHeightFactor))
        {
            return candidate;
        }

        var budget = Math.Max(
            safeTargetUnits + 1,
            Math.Min(effectiveSource.Length, safeTargetUnits * safeMaxLines + 4));
        var minimumBudget = Math.Max(
            4,
            Math.Min(effectiveSource.Length, (int)Math.Ceiling(safeTargetUnits * (safeMaxLines - 0.35))));
        var step = Math.Max(1, safeTargetUnits / 3);

        while (budget > minimumBudget)
        {
            budget -= step;
            candidate = WrapByUnits(
                TruncateAtNaturalBoundary(effectiveSource, budget),
                safeTargetUnits,
                safeMaxLines);

            if (DoesTextFit(candidate, maxWidth, maxHeight, safeMaxLines, minFontSize, minFontWeight, lineHeightFactor))
            {
                return candidate;
            }
        }

        var tightenedUnits = Math.Max(3, safeTargetUnits - 1);
        var tightenedBudget = Math.Max(3, Math.Min(effectiveSource.Length, tightenedUnits * safeMaxLines - 1));
        candidate = WrapByUnits(
            TruncateAtNaturalBoundary(effectiveSource, tightenedBudget),
            tightenedUnits,
            safeMaxLines);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            var fallbackLength = Math.Max(2, Math.Min(effectiveSource.Length, tightenedUnits));
            candidate = WrapByUnits(effectiveSource[..fallbackLength], tightenedUnits, safeMaxLines);
        }

        return candidate;
    }

    private static string WrapByUnits(string? text, int targetUnitsPerLine, int maxLines)
    {
        var normalized = RemoveLineBreaks(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var target = Math.Max(4, targetUnitsPerLine);
        var lineLimit = Math.Max(1, maxLines);
        var clauses = SplitIntoClauses(normalized);

        var lines = new List<string>(lineLimit);
        var current = new StringBuilder();
        var truncated = false;

        foreach (var clause in clauses)
        {
            var remain = clause.Trim();
            while (remain.Length > 0)
            {
                if (lines.Count >= lineLimit)
                {
                    truncated = true;
                    break;
                }

                if (current.Length == 0)
                {
                    if (EstimateDisplayUnits(remain) <= target || lines.Count == lineLimit - 1)
                    {
                        current.Append(remain);
                        remain = string.Empty;
                    }
                    else
                    {
                        var splitIndex = FindSplitIndexByUnits(remain, target);
                        if (splitIndex <= 0 || splitIndex >= remain.Length)
                        {
                            splitIndex = Math.Max(1, remain.Length / 2);
                        }

                        current.Append(remain.AsSpan(0, splitIndex));
                        lines.Add(current.ToString().Trim());
                        current.Clear();
                        remain = remain[splitIndex..].TrimStart();
                    }

                    continue;
                }

                var merged = current + remain;
                if (EstimateDisplayUnits(merged) <= target || lines.Count == lineLimit - 1)
                {
                    current.Append(remain);
                    remain = string.Empty;
                }
                else
                {
                    lines.Add(current.ToString().Trim());
                    current.Clear();
                }
            }

            if (truncated)
            {
                break;
            }
        }

        if (current.Length > 0 && lines.Count < lineLimit)
        {
            lines.Add(current.ToString().Trim());
        }

        lines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (lines.Count == 0)
        {
            lines.Add(normalized);
        }

        if (lines.Count > lineLimit)
        {
            var prefix = lines.Take(lineLimit - 1).ToList();
            var tail = string.Concat(lines.Skip(lineLimit - 1));
            prefix.Add(tail);
            lines = prefix;
            truncated = true;
        }

        if (truncated && lines.Count > 0)
        {
            lines[^1] = AppendEllipsis(lines[^1]);
        }

        return string.Join("\n", lines);
    }

    private static List<string> SplitIntoClauses(string text)
    {
        var clauses = new List<string>();
        var builder = new StringBuilder();

        foreach (var ch in text)
        {
            builder.Append(ch);
            if (NaturalBreakCharSet.Contains(ch))
            {
                clauses.Add(builder.ToString());
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            clauses.Add(builder.ToString());
        }

        return clauses;
    }

    private static int EstimateTargetUnitsPerLine(double width, double scale, bool isNightMode)
    {
        var referenceFont = Math.Clamp((isNightMode ? 20 : 19) * scale, 11, 32);
        var target = (int)Math.Floor(width / Math.Max(7.2, referenceFont * 0.74));
        return Math.Clamp(target, 6, 36);
    }

    private static string TruncateAtNaturalBoundary(string? text, int maxChars)
    {
        var normalized = RemoveLineBreaks(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        var budget = Math.Max(1, maxChars - 1);
        var head = normalized[..Math.Min(budget, normalized.Length)];
        var cut = head.LastIndexOfAny(NaturalBreakChars);
        if (cut < (int)(head.Length * 0.55))
        {
            cut = head.Length;
        }

        var trimmed = head[..Math.Max(1, cut)].TrimEnd(NaturalBreakChars);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = head.Trim();
        }

        return AppendEllipsis(trimmed);
    }

    private static string AppendEllipsis(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "…";
        }

        var trimmed = text.TrimEnd(NaturalBreakChars).TrimEnd();
        return trimmed.EndsWith("…", StringComparison.Ordinal)
            ? trimmed
            : $"{trimmed}…";
    }

    private static bool DoesTextFit(
        string text,
        double maxWidth,
        double maxHeight,
        int maxLines,
        double fontSize,
        FontWeight fontWeight,
        double lineHeightFactor)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var lineHeight = fontSize * lineHeightFactor;
        var measured = MeasureTextSize(text, fontSize, fontWeight, maxWidth, lineHeight);
        var lineCount = Math.Max(1, (int)Math.Ceiling(measured.Height / Math.Max(1, lineHeight)));
        return measured.Height <= maxHeight + 0.6 && lineCount <= Math.Max(1, maxLines);
    }

    private static string NormalizeCompactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return MultiWhitespaceRegex.Replace(text.Trim(), " ");
    }

    private static string RemoveLineBreaks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static int FindSplitIndexByUnits(string text, double targetUnits)
    {
        var units = 0d;
        for (var i = 0; i < text.Length; i++)
        {
            units += text[i] <= 127 ? 0.56 : 1d;
            if (units >= targetUnits)
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    private static double EstimateDisplayUnits(string text)
    {
        var units = 0d;
        foreach (var ch in text)
        {
            units += ch <= 127 ? 0.56 : 1d;
        }

        return units;
    }

    private static TextFitResult FitTextStable(
        string? text,
        double maxWidth,
        double maxHeight,
        double minFontSize,
        double maxFontSize,
        int maxLines,
        double lineHeightFactor,
        double minWeight,
        double maxWeight)
    {
        var normalizedText = string.IsNullOrWhiteSpace(text) ? " " : text.Trim();
        var min = Math.Max(6, minFontSize);
        var max = Math.Max(min, maxFontSize);
        var low = min;
        var high = max;

        var bestSize = min;
        var bestWeight = ToVariableWeight(minWeight);

        for (var i = 0; i < 22; i++)
        {
            var candidate = (low + high) / 2d;
            var progress = max <= min
                ? 0
                : Math.Clamp((candidate - min) / (max - min), 0, 1);
            var candidateWeight = ToVariableWeight(Lerp(minWeight, maxWeight, progress));
            var lineHeight = candidate * lineHeightFactor;

            var measured = MeasureTextSize(normalizedText, candidate, candidateWeight, Math.Max(1, maxWidth), lineHeight);
            var lineCount = Math.Max(1, (int)Math.Ceiling(measured.Height / Math.Max(1, lineHeight)));
            var fits = measured.Height <= maxHeight + 0.6 && lineCount <= Math.Max(1, maxLines);

            if (fits)
            {
                bestSize = candidate;
                bestWeight = candidateWeight;
                low = candidate;
            }
            else
            {
                high = candidate;
            }
        }

        var lineHeightResult = bestSize * lineHeightFactor;
        return new TextFitResult(bestSize, bestWeight, lineHeightResult);
    }

    private static Size MeasureTextSize(
        string text,
        double fontSize,
        FontWeight fontWeight,
        double maxWidth,
        double lineHeight)
    {
        var probe = new TextBlock
        {
            Text = text,
            FontFamily = MiSansFontFamily,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = lineHeight
        };

        probe.Measure(new Size(Math.Max(1, maxWidth), double.PositiveInfinity));
        return probe.DesiredSize;
    }

    private static FontWeight ToVariableWeight(double weight)
    {
        return (FontWeight)(int)Math.Clamp(Math.Round(weight), 1, 1000);
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + (to - from) * Math.Clamp(t, 0, 1);
    }

    private static IBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var r = ToLinear(color.R / 255d);
        var g = ToLinear(color.G / 255d);
        var b = ToLinear(color.B / 255d);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }
}
