using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views.Components;
using Avalonia.Controls.ApplicationLifetimes;

namespace LanMountainDesktop.Views;

public partial class FusedDesktopComponentLibraryControl : UserControl
{
    public event EventHandler<string>? AddComponentRequested;

    private readonly ComponentLibraryWindowViewModel _viewModel = new();
    private List<DesktopComponentDefinition> _allDefinitions = new();

    private ComponentRegistry? _componentRegistry;
    private DesktopComponentRuntimeRegistry? _componentRuntimeRegistry;
    private readonly ISettingsFacadeService _settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
    private readonly IWeatherInfoService _weatherDataService;
    private readonly TimeZoneService _timeZoneService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();

    public FusedDesktopComponentLibraryControl()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _weatherDataService = _settingsFacade.Weather.GetWeatherInfoService();
        _timeZoneService = _settingsFacade.Region.GetTimeZoneService();

        LoadRegistry();
        LoadCategories();

        // 为 ListBoxItem 添加 category-item 样式类
        CategoryListBox.ContainerPrepared += OnCategoryListBoxContainerPrepared;

        // 默认选择第一个分类
        if (_viewModel.Categories.Count > 0)
        {
            CategoryListBox.SelectedIndex = 0;
        }
    }

    private void OnCategoryListBoxContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ListBoxItem listBoxItem)
        {
            listBoxItem.Classes.Add("category-item");
        }
    }

    private void LoadRegistry()
    {
        var pluginRuntimeService = (Application.Current as App)?.PluginRuntimeService;
        _componentRegistry = DesktopComponentRegistryFactory.Create(pluginRuntimeService);
        _componentRuntimeRegistry = DesktopComponentRegistryFactory.CreateRuntimeRegistry(
            _componentRegistry,
            pluginRuntimeService,
            _settingsFacade);

        _allDefinitions = _componentRegistry.GetAll()
            .Where(d => d.AllowDesktopPlacement)
            .ToList();
    }

    private void LoadCategories()
    {
        _viewModel.Categories.Clear();

        // 添加"全部组件"分类
        _viewModel.Categories.Add(new ComponentLibraryCategoryViewModel(
            "all",
            "全部组件",
            Symbol.Apps,
            Array.Empty<ComponentLibraryItemViewModel>()));

        var categoryMap = new Dictionary<string, (string Display, Symbol Icon)>
        {
            { "clock", ("时钟", Symbol.Clock) },
            { "date", ("日历", Symbol.CalendarDate) },
            { "weather", ("天气", Symbol.WeatherSunny) },
            { "board", ("画板", Symbol.Edit) },
            { "media", ("媒体", Symbol.Play) },
            { "info", ("资讯", Symbol.News) },
            { "calculator", ("工具", Symbol.Calculator) },
            { "study", ("学习", Symbol.Hourglass) },
            { "file", ("文件", Symbol.Folder) }
        };

        var usedCategories = _allDefinitions
            .Select(d => d.Category)
            .Distinct()
            .Where(c => !string.IsNullOrEmpty(c));

        foreach (var cat in usedCategories)
        {
            if (categoryMap.TryGetValue(cat.ToLower(), out var info))
            {
                var categoryComponents = _allDefinitions
                    .Where(d => string.Equals(d.Category, cat, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.DisplayName)
                    .Select(d => CreateComponentItem(d))
                    .ToArray();

                _viewModel.Categories.Add(new ComponentLibraryCategoryViewModel(
                    cat,
                    info.Display,
                    info.Icon,
                    categoryComponents));
            }
        }
    }

    private ComponentLibraryItemViewModel CreateComponentItem(DesktopComponentDefinition definition)
    {
        var previewKey = ComponentPreviewKey.ForComponentType(
            definition.Id,
            definition.MinWidthCells,
            definition.MinHeightCells);

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
        ComponentPreviewImageEntry? previewEntry = null;

        if (mainWindow is not null)
        {
            previewEntry = mainWindow.GetPreviewEntry(previewKey);
        }

        var item = new ComponentLibraryItemViewModel(
            definition.Id,
            definition.DisplayName,
            previewKey,
            description: null,
            "正在加载预览...",
            "预览不可用",
            previewEntry);

        if (mainWindow is not null && (previewEntry is null || previewEntry.State == ComponentPreviewImageState.Pending))
        {
            mainWindow.RequestDetachedLibraryPreview(previewKey);
        }

        return item;
    }

    public void UpdatePreviewImage(ComponentPreviewImageEntry entry)
    {
        foreach (var category in _viewModel.Categories)
        {
            foreach (var component in category.Components)
            {
                if (component.PreviewKey.Equals(entry.Key))
                {
                    component.UpdatePreviewImageEntry(entry);
                }
            }
        }
    }

    private void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedComponent();
    }

    private void UpdateSelectedComponent()
    {
        var selectedCategory = CategoryListBox.SelectedItem as ComponentLibraryCategoryViewModel;
        if (selectedCategory is null)
        {
            _viewModel.SelectedComponent = null;
            return;
        }

        // 获取该分类下的组件列表
        IEnumerable<DesktopComponentDefinition> filtered;
        if (selectedCategory.Id == "all")
        {
            filtered = _allDefinitions.OrderBy(d => d.DisplayName);
        }
        else
        {
            filtered = _allDefinitions
                .Where(d => string.Equals(d.Category, selectedCategory.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.DisplayName);
        }

        // 选择该分类下的第一个组件作为默认选中
        var firstComponent = filtered.FirstOrDefault();
        if (firstComponent is not null)
        {
            // 查找或创建对应的 ViewModel
            var existingComponent = selectedCategory.Components.FirstOrDefault(c => c.ComponentId == firstComponent.Id);
            if (existingComponent is not null)
            {
                _viewModel.SelectedComponent = existingComponent;
            }
            else
            {
                _viewModel.SelectedComponent = CreateComponentItem(firstComponent);
            }
        }
        else
        {
            _viewModel.SelectedComponent = null;
        }
    }

    private void OnAddComponentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string componentId)
        {
            AddComponentRequested?.Invoke(this, componentId);
        }
    }
}
