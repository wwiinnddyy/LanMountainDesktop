using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaWebView;
using WebViewCore.Events;

namespace LanMountainDesktop.Views.Components;

public partial class BrowserWidget : UserControl, IDesktopComponentWidget
    , IDesktopPageVisibilityAwareComponentWidget
{
    private static readonly Uri DefaultHomeUri = new("https://www.bing.com");
    private double _currentCellSize = 48;
    private bool? _isNightModeApplied;
    private Uri _lastKnownUri = DefaultHomeUri;
    private bool _isOnActiveDesktopPage;
    private bool _isEditMode;
    private bool _isWebViewActive = true;

    public BrowserWidget()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        ApplyCellSize(_currentCellSize);
        ApplyTheme(force: true);
        BrowserWebView.NavigationStarting += OnBrowserWebViewNavigationStarting;
        UpdateWebViewActiveState();
        NavigateTo(DefaultHomeUri);
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.34, 12, 28));
        RootBorder.Padding = new Thickness(
            Math.Clamp(_currentCellSize * 0.20, 8, 18));

        WebViewHostBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.24, 10, 22));
        AddressBarBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.22, 10, 20));
        AddressBarBorder.Padding = new Thickness(8, 6);

        var rowSpacing = 8d;
        if (RootBorder.Child is Grid rootGrid)
        {
            rootGrid.RowSpacing = rowSpacing;
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

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyTheme(force: true);
        UpdateWebViewActiveState();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isOnActiveDesktopPage = false;
        UpdateWebViewActiveState();
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
        if (!_isWebViewActive)
        {
            return;
        }

        if (BrowserWebView.Url is not null)
        {
            BrowserWebView.Reload();
            return;
        }

        NavigateTo(DefaultHomeUri);
    }

    private void OnGoButtonClick(object? sender, RoutedEventArgs e)
    {
        NavigateFromAddressBar();
    }

    private void OnAddressTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        NavigateFromAddressBar();
        e.Handled = true;
    }

    private void NavigateFromAddressBar()
    {
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
            BrowserWebView.Url = uri;
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

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _isOnActiveDesktopPage = isOnActivePage;
        _isEditMode = isEditMode;
        UpdateWebViewActiveState();
    }

    private void UpdateWebViewActiveState()
    {
        var shouldBeActive = _isOnActiveDesktopPage && !_isEditMode && IsVisible;
        if (_isWebViewActive == shouldBeActive)
        {
            return;
        }

        _isWebViewActive = shouldBeActive;
        if (!_isWebViewActive)
        {
            if (BrowserWebView.Url is Uri currentUri)
            {
                _lastKnownUri = currentUri;
            }

            BrowserWebView.IsHitTestVisible = false;
            BrowserWebView.IsVisible = false;
            BrowserWebView.Url = null;
            return;
        }

        BrowserWebView.IsVisible = true;
        BrowserWebView.IsHitTestVisible = true;
        BrowserWebView.Url = _lastKnownUri;
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
