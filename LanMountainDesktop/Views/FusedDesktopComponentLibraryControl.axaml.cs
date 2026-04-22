using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

    private static readonly LocalizationService _localizationService = new();

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

        var languageCode = _settingsFacade.Region.Get().LanguageCode;

        // 添加"全部组件"分类
        _viewModel.Categories.Add(new ComponentLibraryCategoryViewModel(
            "all",
            L(languageCode, "component_category.all", "All"),
            Symbol.Apps,
            Array.Empty<ComponentLibraryItemViewModel>()));

        var usedCategories = _allDefinitions
            .Select(d => d.Category)
            .Distinct()
            .Where(c => !string.IsNullOrEmpty(c));

        foreach (var cat in usedCategories)
        {
            var icon = ResolveCategoryIcon(cat);
            var title = GetLocalizedCategoryTitle(languageCode, cat);

            var categoryComponents = _allDefinitions
                .Where(d => string.Equals(d.Category, cat, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.DisplayName)
                .Select(d => CreateComponentItem(d))
                .ToArray();

            _viewModel.Categories.Add(new ComponentLibraryCategoryViewModel(
                cat,
                title,
                icon,
                categoryComponents));
        }
    }

    /// <summary>
    /// 分类图标映射 - 与阑山桌面 Dock 栏组件库 (MainWindow.ComponentSystem) 保持一致
    /// </summary>
    private static Symbol ResolveCategoryIcon(string categoryId)
    {
        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase)) return Symbol.Clock;
        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase)) return Symbol.CalendarDate;
        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase)) return Symbol.WeatherSunny;
        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase)) return Symbol.Edit;
        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase)) return Symbol.Play;
        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase)) return Symbol.Apps;
        if (string.Equals(categoryId, "Calculator", StringComparison.OrdinalIgnoreCase)) return Symbol.Calculator;
        if (string.Equals(categoryId, "Study", StringComparison.OrdinalIgnoreCase)) return Symbol.Hourglass;
        if (string.Equals(categoryId, "File", StringComparison.OrdinalIgnoreCase)) return Symbol.Folder;
        return Symbol.Apps;
    }

    /// <summary>
    /// 分类本地化标题 - 与阑山桌面 Dock 栏组件库 (MainWindow.ComponentSystem) 保持一致
    /// </summary>
    private string GetLocalizedCategoryTitle(string languageCode, string categoryId)
    {
        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.clock", "Clock");
        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.date", "Calendar");
        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.weather", "Weather");
        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.board", "Board");
        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.media", "Media");
        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.info", "Info");
        if (string.Equals(categoryId, "Calculator", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.calculator", "Calculator");
        if (string.Equals(categoryId, "Study", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.study", "Study");
        if (string.Equals(categoryId, "File", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.file", "File");
        return categoryId;
    }

    private string L(string languageCode, string key, string fallback)
    {
        return _localizationService.GetString(languageCode, key, fallback);
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

    private void OnFindMoreComponentsClick(object? sender, RoutedEventArgs e)
    {
        // 打开设置窗口并导航到插件目录页面
        if (Application.Current is App app)
        {
            app.OpenIndependentSettingsModule("FusedDesktopComponentLibrary", "plugin-catalog");
        }

        // 关闭所在窗口
        var window = this.FindAncestorOfType<Window>();
        var componentLibraryWindow = this.FindAncestorOfType<Window>();
        componentLibraryWindow?.Close();
    }
}
