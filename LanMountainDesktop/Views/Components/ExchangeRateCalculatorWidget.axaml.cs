using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class ExchangeRateCalculatorWidget : UserControl, IDesktopComponentWidget, IRecommendationInfoAwareComponentWidget, ICalculatorInfoAwareComponentWidget
{
    private sealed record CurrencyItem(string Code, string ZhName, string EnName);

    private static readonly FontFamily MiSansFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
    private static readonly CurrencyItem[] CurrencyItems =
    [
        new("USD", "美元", "US Dollar"),
        new("CNY", "人民币", "Chinese Yuan"),
        new("EUR", "欧元", "Euro"),
        new("JPY", "日元", "Japanese Yen"),
        new("HKD", "港币", "Hong Kong Dollar"),
        new("GBP", "英镑", "British Pound")
    ];

    private static readonly IRecommendationInfoService DefaultRecommendationService = new RecommendationDataService();
    private static readonly ICalculatorDataService DefaultCalculatorService = new CalculatorDataService();

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(30)
    };

    private LanMountainDesktop.PluginSdk.ISettingsService _settingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();
    private IRecommendationInfoService _recommendationService = DefaultRecommendationService;
    private ICalculatorDataService _calculatorDataService = DefaultCalculatorService;

    private string _languageCode = "zh-CN";
    private string _fromCurrency = "USD";
    private string _toCurrency = "CNY";
    private string _inputText = "100";
    private decimal _currentRate = 0m;
    private CancellationTokenSource? _refreshCts;
    private double _currentCellSize = 48d;
    private bool _isAttached;
    private bool _isRefreshing;

    public ExchangeRateCalculatorWidget()
    {
        InitializeComponent();

        FromCurrencyCodeTextBlock.FontFamily = MiSansFontFamily;
        FromCurrencyNameTextBlock.FontFamily = MiSansFontFamily;
        ToCurrencyCodeTextBlock.FontFamily = MiSansFontFamily;
        ToCurrencyNameTextBlock.FontFamily = MiSansFontFamily;
        InputAmountTextBlock.FontFamily = MiSansFontFamily;
        ConvertedAmountTextBlock.FontFamily = MiSansFontFamily;
        RateTextBlock.FontFamily = MiSansFontFamily;
        StatusTextBlock.FontFamily = MiSansFontFamily;

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        _refreshTimer.Tick += OnRefreshTimerTick;

        ApplyCellSize(_currentCellSize);
        UpdateLanguageCode();
        UpdateCurrencyLabels();
        UpdateAmounts();
        ApplyLoadingState();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();
        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(34 * scale, 14, 48);
        RootBorder.Padding = ComponentChromeCornerRadiusHelper.SafeThickness(12 * scale, 12 * scale, null, 0.55d);
    }

    public void SetRecommendationInfoService(IRecommendationInfoService recommendationInfoService)
    {
        _recommendationService = recommendationInfoService ?? DefaultRecommendationService;
        if (_isAttached)
        {
            _ = RefreshExchangeRateAsync(forceRefresh: false);
        }
    }

    public void SetCalculatorDataService(ICalculatorDataService calculatorDataService)
    {
        _calculatorDataService = calculatorDataService ?? DefaultCalculatorService;
        UpdateAmounts();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        _refreshTimer.Start();
        _ = RefreshExchangeRateAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        CancelRefreshRequest();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshExchangeRateAsync(forceRefresh: false);
    }

    private async void OnSwapCurrencyButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        var from = _fromCurrency;
        _fromCurrency = _toCurrency;
        _toCurrency = from;
        UpdateCurrencyLabels();
        await RefreshExchangeRateAsync(forceRefresh: false);
    }

    private async void OnFromCurrencyRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _fromCurrency = GetNextCurrencyCode(_fromCurrency, _toCurrency);
        UpdateCurrencyLabels();
        await RefreshExchangeRateAsync(forceRefresh: false);
        e.Handled = true;
    }

    private async void OnToCurrencyRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _toCurrency = GetNextCurrencyCode(_toCurrency, _fromCurrency);
        UpdateCurrencyLabels();
        await RefreshExchangeRateAsync(forceRefresh: false);
        e.Handled = true;
    }

    private void OnInputButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is null)
        {
            return;
        }

        var token = button.Tag.ToString() ?? string.Empty;
        _inputText = _calculatorDataService.ApplyInputToken(_inputText, token);
        UpdateAmounts();
        e.Handled = true;
    }

    private async Task RefreshExchangeRateAsync(bool forceRefresh)
    {
        if (!_isAttached || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        UpdateLanguageCode();

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var query = new ExchangeRateQuery(
                BaseCurrency: _fromCurrency,
                TargetCurrency: _toCurrency,
                ForceRefresh: forceRefresh);
            var result = await _recommendationService.GetExchangeRateAsync(query, cts.Token);
            if (!_isAttached || cts.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                ApplyFailedState();
                return;
            }

            _currentRate = result.Data.Rate;
            StatusTextBlock.IsVisible = false;
            UpdateAmounts();
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
        }
    }

    private void UpdateCurrencyLabels()
    {
        var from = ResolveCurrency(_fromCurrency);
        var to = ResolveCurrency(_toCurrency);

        FromCurrencyCodeTextBlock.Text = from.Code;
        FromCurrencyNameTextBlock.Text = IsZh() ? from.ZhName : from.EnName;
        ToCurrencyCodeTextBlock.Text = to.Code;
        ToCurrencyNameTextBlock.Text = IsZh() ? to.ZhName : to.EnName;
    }

    private void UpdateAmounts()
    {
        var amount = _calculatorDataService.ParseAmountOrZero(_inputText);
        var converted = amount * Math.Max(0m, _currentRate);

        InputAmountTextBlock.Text = _inputText;
        ConvertedAmountTextBlock.Text = _calculatorDataService.FormatAmount(converted, maxFractionDigits: 4);
        RateTextBlock.Text = string.Format(
            CultureInfo.InvariantCulture,
            "1 {0} = {1} {2}",
            _fromCurrency,
            _calculatorDataService.FormatAmount(_currentRate, maxFractionDigits: 6),
            _toCurrency);
    }

    private void ApplyLoadingState()
    {
        StatusTextBlock.Text = L("exchange.widget.loading", "正在加载汇率...");
        StatusTextBlock.IsVisible = true;
    }

    private void ApplyFailedState()
    {
        StatusTextBlock.Text = L("exchange.widget.fetch_failed", "汇率获取失败");
        StatusTextBlock.IsVisible = true;
        UpdateAmounts();
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

    private string GetNextCurrencyCode(string current, string avoid)
    {
        var currentIndex = Array.FindIndex(
            CurrencyItems,
            item => string.Equals(item.Code, current, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        for (var step = 1; step <= CurrencyItems.Length; step++)
        {
            var next = CurrencyItems[(currentIndex + step) % CurrencyItems.Length].Code;
            if (!string.Equals(next, avoid, StringComparison.OrdinalIgnoreCase))
            {
                return next;
            }
        }

        return current;
    }

    private static CurrencyItem ResolveCurrency(string code)
    {
        return CurrencyItems.FirstOrDefault(item =>
            string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
            ?? CurrencyItems[0];
    }

    private bool IsZh()
    {
        return string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.72, 1.8);
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 304d, 0.72, 2.0) : 1;
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 304d, 0.72, 2.0) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale)), 0.72, 1.95);
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
}
