using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaWebView;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using WebViewCore.Events;

namespace LanMountainDesktop.Views.Components;

public partial class BrowserWidget : UserControl, IDesktopComponentWidget,
    IDesktopPageVisibilityAwareComponentWidget, IComponentPlacementContextAware, IDisposable
{
    private static readonly Uri DefaultHomeUri = new("https://www.bing.com");

    private readonly bool _isDesignModePreview = Design.IsDesignMode;
    private double _currentCellSize = 48;
    private string _componentId = BuiltInComponentIds.DesktopBrowser;
    private string _placementId = string.Empty;
    private bool? _isNightModeApplied;
    private Uri _lastKnownUri = DefaultHomeUri;
    private bool _isOnActiveDesktopPage;
    private bool _isAttachedToVisualTree;
    private bool _isEditMode;
    private bool _isWebViewActive = true;
    private bool _isWebViewFaulted;
    private WebView? _browserWebView;
    private readonly WebView2RuntimeAvailability _runtimeAvailability;
    private bool _isDisposed;

    public BrowserWidget()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        ApplyCellSize(_currentCellSize);
        ApplyTheme(force: true);

        _runtimeAvailability = _isDesignModePreview
            ? new WebView2RuntimeAvailability(
                IsAvailable: false,
                Version: null,
                Message: "WebView preview is disabled in Avalonia design mode.")
            : WebView2RuntimeProbe.GetAvailability();
        if (_runtimeAvailability.IsAvailable)
        {
            EnsureWebViewCreated();
        }
        else
        {
            ApplyRuntimeUnavailableState();
        }

        AddressTextBox.Text = DefaultHomeUri.ToString();
        UpdateWebViewActiveState();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        SizeChanged -= OnSizeChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;

        if (_browserWebView is not null)
        {
            _browserWebView.NavigationStarting -= OnBrowserWebViewNavigationStarting;
        }
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);

        var mainRectangleCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.CornerRadius = mainRectangleCornerRadius;
        RootBorder.Padding = new Thickness(Math.Clamp(_currentCellSize * 0.20, 8, 18));

        WebViewHostBorder.CornerRadius = mainRectangleCornerRadius;
        AddressBarBorder.CornerRadius = mainRectangleCornerRadius;
        AddressBarBorder.Padding = new Thickness(8, 6);

        if (RootBorder.Child is Grid rootGrid)
        {
            rootGrid.RowSpacing = 8d;
        }

        var buttonSize = Math.Clamp(_currentCellSize * 0.72, 30, 36);
        var buttonCorner = buttonSize * 0.5;
        var iconSize = Math.Clamp(buttonSize * 0.44, 14, 16);
        foreach (var button in new[] { RefreshButton, GoButton })
        {
            button.Width = buttonSize;
            button.Height = buttonSize;
            button.CornerRadius = new CornerRadius(buttonCorner);
        }

        if (RefreshButton.Content is FluentIcons.Avalonia.SymbolIcon refreshIcon)
        {
            refreshIcon.FontSize = iconSize;
        }

        if (GoButton.Content is FluentIcons.Avalonia.SymbolIcon goIcon)
        {
            goIcon.FontSize = iconSize;
        }

        AddressTextBox.FontSize = Math.Clamp(_currentCellSize * 0.30, 12, 15);
        AddressTextBox.Height = buttonSize;
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _isOnActiveDesktopPage = isOnActivePage;
        _isEditMode = isEditMode;
        UpdateWebViewActiveState();
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopBrowser
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = true;
        ApplyTheme(force: true);
        UpdateWebViewActiveState();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        _isOnActiveDesktopPage = false;
        DeactivateWebView(clearUrl: false);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ApplyTheme(force: false);
    }

    private void ApplyTheme(bool force)
    {
        var isNightMode = ResolveIsNightMode();
        if (!force && _isNightModeApplied.HasValue && _isNightModeApplied.Value == isNightMode)
        {
            return;
        }

        _isNightModeApplied = isNightMode;
        RootBorder.Background = new SolidColorBrush(isNightMode ? Color.Parse("#FF141A24") : Color.Parse("#FFF4F7FC"));
        WebViewHostBorder.Background = new SolidColorBrush(isNightMode ? Color.Parse("#FF0A0E15") : Color.Parse("#FFFFFFFF"));
        WebViewHostBorder.BorderBrush = new SolidColorBrush(isNightMode ? Color.Parse("#33FFFFFF") : Color.Parse("#22000000"));
        AddressBarBorder.Background = new SolidColorBrush(isNightMode ? Color.Parse("#1BFFFFFF") : Color.Parse("#ECF2FA"));
        AddressBarBorder.BorderBrush = new SolidColorBrush(isNightMode ? Color.Parse("#26FFFFFF") : Color.Parse("#22000000"));

        var idleBackground = new SolidColorBrush(isNightMode ? Color.Parse("#24FFFFFF") : Color.Parse("#DCE6F5"));
        var idleForeground = new SolidColorBrush(isNightMode ? Color.Parse("#FFE5E7EB") : Color.Parse("#FF1E293B"));

        foreach (var button in new[] { RefreshButton, GoButton })
        {
            button.Background = idleBackground;
            button.Foreground = idleForeground;
            button.BorderThickness = new Thickness(0);
        }

        AddressTextBox.Background = new SolidColorBrush(isNightMode ? Color.Parse("#1F000000") : Color.Parse("#FFFFFFFF"));
        AddressTextBox.BorderBrush = new SolidColorBrush(isNightMode ? Color.Parse("#2FFFFFFF") : Color.Parse("#22000000"));
        AddressTextBox.Foreground = idleForeground;
        AddressTextBox.CaretBrush = idleForeground;
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
            value is ISolidColorBrush brush)
        {
            return CalculateRelativeLuminance(brush.Color) < 0.45;
        }

        return false;
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var red = ToLinear(color.R / 255d);
        var green = ToLinear(color.G / 255d);
        var blue = ToLinear(color.B / 255d);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        if (!CanUseWebView())
        {
            return;
        }

        if (!TryReloadWebView("Refresh"))
        {
            TryNavigate(DefaultHomeUri, "RefreshFallback");
        }
    }

    private void OnGoButtonClick(object? sender, RoutedEventArgs e)
    {
        if (!CanUseWebView())
        {
            return;
        }

        NavigateFromAddressBar();
    }

    private void OnAddressTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (!CanUseWebView())
        {
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        NavigateFromAddressBar();
        e.Handled = true;
    }

    private void NavigateFromAddressBar()
    {
        if (!CanUseWebView())
        {
            return;
        }

        var target = TryNormalizeUri(AddressTextBox.Text);
        if (target is null)
        {
            return;
        }

        NavigateTo(target);
    }

    private void NavigateTo(Uri uri)
    {
        _lastKnownUri = uri;
        AddressTextBox.Text = uri.ToString();
        if (_isWebViewActive)
        {
            TryNavigate(uri, "NavigateTo");
        }
    }

    private void OnBrowserWebViewNavigationStarting(object? sender, WebViewUrlLoadingEventArg e)
    {
        if (e.Url is null)
        {
            return;
        }

        _lastKnownUri = e.Url;
        AddressTextBox.Text = e.Url.ToString();
    }

    private void UpdateWebViewActiveState()
    {
        if (_isDesignModePreview)
        {
            _isWebViewActive = false;
            ApplyRuntimeUnavailableState();
            return;
        }

        if (!_runtimeAvailability.IsAvailable || _isWebViewFaulted)
        {
            _isWebViewActive = false;
            ApplyRuntimeUnavailableState();
            return;
        }

        var shouldBeActive = _isAttachedToVisualTree && _isOnActiveDesktopPage && !_isEditMode && IsVisible;
        if (_isWebViewActive == shouldBeActive)
        {
            return;
        }

        _isWebViewActive = shouldBeActive;
        if (!_isWebViewActive)
        {
            DeactivateWebView(clearUrl: false);
            return;
        }

        ActivateWebView();
    }

    private void ActivateWebView()
    {
        EnsureWebViewCreated();
        if (_isWebViewFaulted || !_runtimeAvailability.IsAvailable)
        {
            ApplyRuntimeUnavailableState();
            return;
        }

        if (_browserWebView is null)
        {
            ApplyRuntimeUnavailableState();
            return;
        }

        _browserWebView.IsVisible = true;
        _browserWebView.IsHitTestVisible = true;
        RefreshButton.IsEnabled = true;
        GoButton.IsEnabled = true;
        AddressTextBox.IsEnabled = true;
        UnavailableOverlay.IsVisible = false;
    }

    private void DeactivateWebView(bool clearUrl)
    {
        if (_browserWebView is not null)
        {
            _browserWebView.IsHitTestVisible = false;
            _browserWebView.IsVisible = false;
        }

        if (clearUrl)
        {
            TryClearWebViewUrl();
        }
    }

    private bool TryReloadWebView(string action)
    {
        if (_browserWebView is null)
        {
            return false;
        }

        try
        {
            _browserWebView.Reload();
            return true;
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            EnterFaultedState(action, ex);
            return false;
        }
    }

    private bool TryNavigate(Uri uri, string action)
    {
        if (_browserWebView is null)
        {
            return false;
        }

        try
        {
            _browserWebView.Url = uri;
            return true;
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            EnterFaultedState(action, ex);
            return false;
        }
    }

    private void TryClearWebViewUrl()
    {
        if (_browserWebView is null)
        {
            return;
        }

        try
        {
            _browserWebView.Url = null;
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private bool CanUseWebView()
    {
        return _runtimeAvailability.IsAvailable &&
               !_isWebViewFaulted &&
               _isWebViewActive &&
               _browserWebView is not null;
    }

    private void ApplyRuntimeUnavailableState()
    {
        _isWebViewActive = false;
        if (_browserWebView is not null)
        {
            _browserWebView.IsVisible = false;
            _browserWebView.IsHitTestVisible = false;
        }

        RefreshButton.IsEnabled = false;
        GoButton.IsEnabled = false;
        AddressTextBox.IsEnabled = false;
        AddressTextBox.Text = _lastKnownUri.ToString();

        UnavailableMessageTextBlock.Text = _isWebViewFaulted
            ? "The browser component is temporarily unavailable. Restart the app to retry."
            : string.IsNullOrWhiteSpace(_runtimeAvailability.Message)
                ? "WebView runtime unavailable."
                : _runtimeAvailability.Message;
        UnavailableOverlay.IsVisible = true;
    }

    private void EnsureWebViewCreated()
    {
        if (_browserWebView is not null || _isDesignModePreview || !_runtimeAvailability.IsAvailable)
        {
            return;
        }

        _browserWebView = new WebView
        {
            IsVisible = false,
            IsHitTestVisible = false
        };
        _browserWebView.NavigationStarting += OnBrowserWebViewNavigationStarting;
        WebViewPresenter.Children.Insert(0, _browserWebView);
    }

    private void EnterFaultedState(string action, Exception ex)
    {
        _isWebViewFaulted = true;
        _isWebViewActive = false;
        AppLogger.Warn(
            "BrowserWidget",
            $"Browser component faulted. Action={action}; ComponentId={_componentId}; PlacementId={_placementId}; RuntimeAvailability={_runtimeAvailability.IsAvailable}; RuntimeVersion={_runtimeAvailability.Version ?? string.Empty}; CurrentUrl={_lastKnownUri}",
            ex);
        TryClearWebViewUrl();
        ApplyRuntimeUnavailableState();
    }

    private static Uri? TryNormalizeUri(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var candidate = rawText.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }
}
