using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "data",
    "数据",
    SettingsPageCategory.General,
    IconKey = "HardDrive",
    SortOrder = 5,
    TitleLocalizationKey = "settings.data.title",
    DescriptionLocalizationKey = "settings.data.description")]
public partial class DataSettingsPage : SettingsPageBase
{
    private readonly SolidColorBrush _fallbackBrush = new(Colors.Gray);

    public DataSettingsPage()
        : this(new DataSettingsPageViewModel())
    {
    }

    public DataSettingsPage(DataSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        AttachStorageBarObservers();
        RebuildStorageBar();
    }

    public DataSettingsPageViewModel ViewModel { get; }

    private void AttachStorageBarObservers()
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        foreach (var item in ViewModel.Items)
        {
            item.PropertyChanged += OnStorageItemPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(DataSettingsPageViewModel.Items), StringComparison.Ordinal))
        {
            RebuildStorageBar();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(DataSettingsPageViewModel.HasData), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(DataSettingsPageViewModel.DiskUsagePercentage), StringComparison.Ordinal))
        {
            RebuildStorageBar();
        }
    }

    private void OnStorageItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(DataStorageItemViewModel.Percentage), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(DataStorageItemViewModel.ColorHex), StringComparison.Ordinal))
        {
            RebuildStorageBar();
        }
    }

    private void RebuildStorageBar()
    {
        if (StorageBarGrid is null)
        {
            return;
        }

        StorageBarGrid.ColumnDefinitions.Clear();
        StorageBarGrid.Children.Clear();

        var visibleItems = ViewModel.Items
            .Where(item => item.Percentage > 0)
            .OrderByDescending(item => item.Percentage)
            .ToList();

        var idx = 0;
        foreach (var item in visibleItems)
        {
            var width = Math.Max(0.1, item.Percentage);
            StorageBarGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(width, GridUnitType.Star)));
            var segment = new Border
            {
                Background = ParseBrush(item.ColorHex),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(segment, idx++);
            StorageBarGrid.Children.Add(segment);
        }

        var remaining = 100d - ViewModel.DiskUsagePercentage;
        if (remaining > 0)
        {
            StorageBarGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(remaining, GridUnitType.Star)));
        }
    }

    private IBrush ParseBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return _fallbackBrush;
        }

        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch
        {
            return _fallbackBrush;
        }
    }
}
