using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.ViewModels;

public sealed partial class DataStorageItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string ColorHex { get; }
    public bool IsCleanable { get; }

    [ObservableProperty]
    private string _sizeText = "--";

    [ObservableProperty]
    private double _percentage;

    [ObservableProperty]
    private bool _isCleaning;

    public DataStorageItemViewModel(StorageCategoryInfo category)
    {
        Id = category.Id;
        Name = category.Name;
        Description = category.Description;
        ColorHex = category.ColorHex;
        IsCleanable = category.IsCleanable;
    }

    public void UpdateSize(long sizeBytes, double percentage)
    {
        SizeText = DataStorageService.FormatBytes(sizeBytes);
        Percentage = percentage;
    }
}

public sealed partial class DataSettingsPageViewModel : ViewModelBase
{
    private readonly DataStorageService _storageService = new();
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private string _pageTitle = "数据";

    [ObservableProperty]
    private string _totalSizeText = "--";

    [ObservableProperty]
    private string _diskUsageText = "--";

    [ObservableProperty]
    private double _diskUsagePercentage;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasData;

    public ObservableCollection<DataStorageItemViewModel> Items { get; } = new();

    public DataSettingsPageViewModel()
    {
        var categories = _storageService.GetCategories();
        foreach (var category in categories)
        {
            Items.Add(new DataStorageItemViewModel(category));
        }

        _ = ScanAsync();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        IsScanning = true;
        try
        {
            var results = await _storageService.ScanAsync(token);
            var totalSize = results.Sum(r => r.SizeBytes);
            var totalDisk = await _storageService.GetTotalDiskSpaceAsync(token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TotalSizeText = DataStorageService.FormatBytes(totalSize);
                DiskUsagePercentage = totalDisk > 0 ? (double)totalSize / totalDisk * 100 : 0;
                DiskUsageText = $"占总磁盘 {DiskUsagePercentage:F1}%";
                HasData = totalSize > 0;

                foreach (var result in results)
                {
                    var item = Items.FirstOrDefault(i =>
                        string.Equals(i.Id, result.Category.Id, StringComparison.OrdinalIgnoreCase));
                    item?.UpdateSize(result.SizeBytes, result.PercentageOfTotal);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DataSettings", "Failed to scan storage.", ex);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task CleanAsync(string categoryId)
    {
        var item = Items.FirstOrDefault(i =>
            string.Equals(i.Id, categoryId, StringComparison.OrdinalIgnoreCase));
        if (item is null || !item.IsCleanable)
        {
            return;
        }

        item.IsCleaning = true;
        try
        {
            await _storageService.CleanCategoryAsync(categoryId);
            await ScanAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DataSettings", $"Failed to clean category '{categoryId}'.", ex);
        }
        finally
        {
            item.IsCleaning = false;
        }
    }

    [RelayCommand]
    private async Task CleanAllAsync()
    {
        foreach (var item in Items.Where(i => i.IsCleanable))
        {
            item.IsCleaning = true;
        }

        try
        {
            foreach (var item in Items.Where(i => i.IsCleanable))
            {
                await _storageService.CleanCategoryAsync(item.Id);
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DataSettings", "Failed to clean all categories.", ex);
        }
        finally
        {
            foreach (var item in Items)
            {
                item.IsCleaning = false;
            }
        }
    }
}
