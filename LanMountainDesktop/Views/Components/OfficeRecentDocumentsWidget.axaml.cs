using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views.Components;

public partial class OfficeRecentDocumentsWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget
{
    private readonly IOfficeRecentDocumentsService _recentDocumentsService;
    private List<OfficeRecentDocument> _documents = new();
    private bool _isOnActivePage;
    private bool _isEditMode;
    private bool _isLoading;

    public OfficeRecentDocumentsWidget()
    {
        InitializeComponent();
        _recentDocumentsService = new OfficeRecentDocumentsService();
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

    private void LoadDocuments()
    {
        try
        {
            _isLoading = true;
            StatusTextBlock.IsVisible = false;

            _documents = _recentDocumentsService.GetRecentDocuments(20);

            if (_documents.Count == 0)
            {
                StatusTextBlock.Text = "暂无最近文档";
                StatusTextBlock.IsVisible = true;
                return;
            }

            UpdateDisplay();
        }
        catch
        {
            StatusTextBlock.Text = "加载失败";
            StatusTextBlock.IsVisible = true;
        }
        finally
        {
            _isLoading = false;
        }
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
            return "刚刚";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays} 天前";
        if (span.TotalDays < 30)
            return $"{(int)(span.TotalDays / 7)} 周前";

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
