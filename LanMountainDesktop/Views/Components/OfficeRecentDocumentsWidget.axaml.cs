using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LanMountainDesktop.DesktopComponents.Runtime;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class OfficeRecentDocumentsWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, IComponentPlacementContextAware
{
    private readonly IOfficeRecentDocumentsService _recentDocumentsService;
    private readonly IComponentInstanceSettingsStore _componentSettingsStore = HostComponentSettingsStoreProvider.GetOrCreate();
    private List<OfficeRecentDocument> _documents = new();
    private string _componentId = BuiltInComponentIds.DesktopOfficeRecentDocuments;
    private string _placementId = string.Empty;
    private IReadOnlyList<string> _enabledSources = OfficeRecentDocumentSourceTypes.DefaultValues;
    private bool _isOnActivePage;
    private bool _isEditMode;
    private bool _isLoading;

    public OfficeRecentDocumentsWidget()
    {
        InitializeComponent();
        _recentDocumentsService = new OfficeRecentDocumentsService();
        ReloadSettings();
    }

    public void ApplyCellSize(double cellSize)
    {
        if (RootBorder is null)
        {
            return;
        }

        var normalizedCellSize = Math.Max(1, cellSize);
        var scale = Math.Clamp(normalizedCellSize / 48d, 0.72d, 1.65d);
        var rootCornerRadius = ComponentChromeCornerRadiusHelper.Scale(34 * scale, 16, 52);

        RootBorder.CornerRadius = rootCornerRadius;
        RootBorder.Padding = ComponentChromeCornerRadiusHelper.SafeThickness(
            10 * scale,
            8 * scale,
            null,
            0.45d);

        Resources["OfficeRecentDocumentsRootCornerRadius"] = rootCornerRadius;
        Resources["OfficeRecentDocumentsRootPadding"] = RootBorder.Padding;

        var contentMargin = ComponentChromeCornerRadiusHelper.SafeThickness(
            16 * scale,
            14 * scale,
            null,
            0.55d);
        Resources["OfficeRecentDocumentsContentMargin"] = contentMargin;
        Resources["OfficeRecentDocumentsScrollMargin"] = new Thickness(
            0,
            ComponentChromeCornerRadiusHelper.SafeValue(4 * scale, 2, 8, null, 0.40d),
            0,
            0);
        Resources["OfficeRecentDocumentsContentRowSpacing"] = ComponentChromeCornerRadiusHelper.SafeValue(8 * scale, 4, 12, null, 0.40d);

        var refreshButtonSize = Math.Clamp(28 * scale, 20, 40);
        Resources["OfficeRecentDocumentsRefreshButtonSize"] = refreshButtonSize;
        Resources["OfficeRecentDocumentsRefreshCornerRadius"] = new CornerRadius(refreshButtonSize / 2d);
        Resources["OfficeRecentDocumentsRefreshIconFontSize"] = Math.Clamp(14 * scale, 10, 20);

        var accentSize = Math.Clamp(140 * scale, 88, 188);
        Resources["OfficeRecentDocumentsAccentSize"] = accentSize;
        Resources["OfficeRecentDocumentsAccentCornerRadius"] = new CornerRadius(accentSize / 2d);

        Resources["OfficeRecentDocumentsDocumentSpacing"] = ComponentChromeCornerRadiusHelper.SafeValue(8 * scale, 4, 12, null, 0.40d);

        var cardWidth = Math.Clamp(130 * scale, 96, 180);
        var cardHeight = Math.Clamp(90 * scale, 68, 124);
        Resources["OfficeRecentDocumentsDocumentCardWidth"] = cardWidth;
        Resources["OfficeRecentDocumentsDocumentCardHeight"] = cardHeight;
        Resources["OfficeRecentDocumentsCardCornerRadius"] = ComponentChromeCornerRadiusHelper.Scale(16 * scale, 10, 24);
        Resources["OfficeRecentDocumentsCardPadding"] = new Thickness(ComponentChromeCornerRadiusHelper.SafeValue(10 * scale, 6, 16, null, 0.50d));
        UpdateTypographyResources();
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _isOnActivePage = isOnActivePage;
        _isEditMode = isEditMode;

        if (_isOnActivePage && !_isLoading)
        {
            LoadDocuments();
        }
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopOfficeRecentDocuments
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        ReloadSettings();
    }

    private async void LoadDocuments()
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            _isLoading = true;
            ReloadSettings();
            StatusTextBlock.IsVisible = false;
            DocumentsItemsControl.ItemsSource = null;

            var enabledSources = _enabledSources.ToArray();
            _documents = await Task.Run(() => _recentDocumentsService.GetRecentDocuments(20, enabledSources));

            if (_documents.Count == 0)
            {
                StatusTextBlock.Text = "\u6682\u65e0\u6700\u8fd1\u6587\u6863";
                StatusTextBlock.IsVisible = true;
                UpdateTypographyResources();
                return;
            }

            UpdateDisplay();
            UpdateTypographyResources();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("OfficeRecentDocsWidget", "Failed to load recent Office documents.", ex);
            StatusTextBlock.Text = "\u52a0\u8f7d\u5931\u8d25";
            StatusTextBlock.IsVisible = true;
            UpdateTypographyResources();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ReloadSettings()
    {
        var snapshot = _componentSettingsStore.LoadForComponent(_componentId, _placementId);
        _enabledSources = OfficeRecentDocumentSourceTypes.NormalizeValues(
            snapshot.OfficeRecentDocumentsEnabledSources,
            useDefaultWhenEmpty: snapshot.OfficeRecentDocumentsEnabledSources is null);
    }

    private void UpdateDisplay()
    {
        var displayItems = _documents.Select(d => new OfficeRecentDocumentViewModel
        {
            FileName = d.FileName,
            FilePath = d.FilePath,
            TimeAgo = GetTimeAgo(d.LastModifiedTime)
        }).ToList();

        DocumentsItemsControl.ItemsSource = displayItems;
        UpdateTypographyResources();
    }

    private static string GetTimeAgo(DateTime dateTime)
    {
        var span = DateTime.Now - dateTime;

        if (span.TotalMinutes < 1)
        {
            return "\u521a\u521a";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes} \u5206\u949f\u524d";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours} \u5c0f\u65f6\u524d";
        }

        if (span.TotalDays < 7)
        {
            return $"{(int)span.TotalDays} \u5929\u524d";
        }

        if (span.TotalDays < 30)
        {
            return $"{(int)(span.TotalDays / 7)} \u5468\u524d";
        }

        return dateTime.ToString("MM/dd");
    }

    private void OnRefreshPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        LoadDocuments();
    }

    private void OnDocumentCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is { } data)
        {
            var filePathProperty = data.GetType().GetProperty("FilePath");
            var filePath = filePathProperty?.GetValue(data) as string;

            if (!string.IsNullOrEmpty(filePath))
            {
                _recentDocumentsService.OpenDocument(filePath);
            }
        }
    }

    private void UpdateTypographyResources()
    {
        var width = Bounds.Width > 1 ? Bounds.Width : 640d;
        var cardWidth = (double?)Resources["OfficeRecentDocumentsDocumentCardWidth"] ?? 130d;
        var cardHeight = (double?)Resources["OfficeRecentDocumentsDocumentCardHeight"] ?? 90d;
        var cardPadding = (Thickness?)Resources["OfficeRecentDocumentsCardPadding"] ?? new Thickness(10);
        var rootPadding = (Thickness?)Resources["OfficeRecentDocumentsRootPadding"] ?? new Thickness(12, 10, 12, 10);
        var contentMargin = (Thickness?)Resources["OfficeRecentDocumentsContentMargin"] ?? new Thickness(16, 14, 16, 14);

        var innerWidth = Math.Max(180, width - rootPadding.Left - rootPadding.Right - contentMargin.Left - contentMargin.Right);
        var headerWidth = Math.Max(120, innerWidth * 0.48);
        var statusWidth = Math.Max(120, innerWidth * 0.40);

        Resources["OfficeRecentDocumentsHeaderFontSize"] = ComponentTypographyLayoutService.FitFontSize(
            HeaderTextBlock.Text,
            headerWidth,
            24,
            1,
            12,
            24,
            FontWeight.SemiBold,
            1.05d);

        Resources["OfficeRecentDocumentsStatusFontSize"] = ComponentTypographyLayoutService.FitFontSize(
            StatusTextBlock.Text,
            statusWidth,
            22,
            1,
            10,
            18,
            FontWeight.Normal,
            1.06d);

        var documentTexts = _documents.Count == 0
            ? new[] { "Sample Office Document" }
            : _documents.Select(item => item.FileName).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        var longestDocumentText = documentTexts.Length == 0
            ? "Sample Office Document"
            : documentTexts.OrderByDescending(ComponentTypographyLayoutService.CountTextDisplayUnits).First();
        var titleWidth = Math.Max(72, cardWidth - cardPadding.Left - cardPadding.Right);
        var titleHeight = Math.Max(28, cardHeight - cardPadding.Top - cardPadding.Bottom - 18);
        Resources["OfficeRecentDocumentsDocumentTitleFontSize"] = ComponentTypographyLayoutService.FitFontSize(
            longestDocumentText,
            titleWidth,
            titleHeight,
            2,
            10,
            18,
            FontWeight.Medium,
            1.08d);

        var timeSamples = _documents.Count == 0
            ? new[] { "00/00" }
            : _documents.Select(item => GetTimeAgo(item.LastModifiedTime)).ToArray();
        var longestTimeText = timeSamples.OrderByDescending(ComponentTypographyLayoutService.CountTextDisplayUnits).First();
        Resources["OfficeRecentDocumentsDocumentTimeFontSize"] = ComponentTypographyLayoutService.FitFontSize(
            longestTimeText,
            Math.Max(56, titleWidth * 0.72),
            18,
            1,
            8,
            14,
            FontWeight.Normal,
            1.06d);
    }
}
