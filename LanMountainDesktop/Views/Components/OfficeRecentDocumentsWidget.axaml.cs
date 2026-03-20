using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
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

        var scale = cellSize / 100.0;
        RootBorder.CornerRadius = new Avalonia.CornerRadius(Math.Max(8, 34 * scale));
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
                return;
            }

            UpdateDisplay();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("OfficeRecentDocsWidget", "Failed to load recent Office documents.", ex);
            StatusTextBlock.Text = "\u52a0\u8f7d\u5931\u8d25";
            StatusTextBlock.IsVisible = true;
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
}
