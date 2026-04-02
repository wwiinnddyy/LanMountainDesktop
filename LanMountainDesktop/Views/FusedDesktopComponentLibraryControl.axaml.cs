using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views.Components;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views;

public partial class FusedDesktopComponentLibraryControl : UserControl
{
    public event EventHandler<string>? AddComponentRequested;

    private readonly ObservableCollection<LibraryCategoryItem> _categories = new();
    private readonly ObservableCollection<LibraryComponentItem> _components = new();
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
        _weatherDataService = _settingsFacade.Weather.GetWeatherInfoService();
        _timeZoneService = _settingsFacade.Region.GetTimeZoneService();
        
        CategoryListBox.ItemsSource = _categories;
        ComponentItemsControl.ItemsSource = _components;
        
        LoadRegistry();
        LoadCategories();
        SearchBox.KeyUp += (s, e) => FilterComponents();
        
        // 默认选择第一个分类
        if (_categories.Count > 0)
        {
            CategoryListBox.SelectedIndex = 0;
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
        _categories.Clear();
        _categories.Add(new LibraryCategoryItem("all", "全部组件", "Apps"));
        
        var categoryMap = new Dictionary<string, (string Display, string Icon)>
        {
            { "clock", ("时钟", "Clock") },
            { "date", ("日历", "Calendar") },
            { "weather", ("天气", "WeatherCloudy") },
            { "info", ("资讯", "News") },
            { "calculator", ("工具", "Calculator") },
            { "study", ("学习", "Book") },
            { "file", ("文件", "Document") }
        };

        var usedCategories = _allDefinitions
            .Select(d => d.Category)
            .Distinct()
            .Where(c => !string.IsNullOrEmpty(c));

        foreach (var cat in usedCategories)
        {
            if (categoryMap.TryGetValue(cat.ToLower(), out var info))
            {
                _categories.Add(new LibraryCategoryItem(cat, info.Display, info.Icon));
            }
            else
            {
                _categories.Add(new LibraryCategoryItem(cat, cat, "Cube"));
            }
        }
    }

    private void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        FilterComponents();
    }

    private void FilterComponents()
    {
        var selectedCategory = (CategoryListBox.SelectedItem as LibraryCategoryItem)?.Id;
        var searchText = SearchBox.Text?.ToLower() ?? "";

        var filtered = _allDefinitions.Where(d =>
        {
            var matchesCategory = selectedCategory == "all" || string.Equals(d.Category, selectedCategory, StringComparison.OrdinalIgnoreCase);
            var matchesSearch = string.IsNullOrEmpty(searchText) || d.DisplayName.ToLower().Contains(searchText) || d.Id.ToLower().Contains(searchText);
            return matchesCategory && matchesSearch;
        });

        _components.Clear();
        foreach (var def in filtered)
        {
            _components.Add(new LibraryComponentItem
            {
                Id = def.Id,
                DisplayName = def.DisplayName,
                Description = GetDescription(def.Id),
                PreviewContent = CreatePreview(def.Id)
            });
        }
    }

    private string GetDescription(string id)
    {
        // 简单映射描述信息
        return id.Contains("clock") ? "实时显示当前时间与日期。" :
               id.Contains("weather") ? "为您提供精准的天气预报。" :
               "多功能桌面组件，提升您的操作效率。";
    }

    private Control? CreatePreview(string id)
    {
        if (_componentRuntimeRegistry == null || !_componentRuntimeRegistry.TryGetDescriptor(id, out var descriptor))
        {
            return null;
        }

        try
        {
            var control = descriptor.CreateControl(
                100, // Previews assume 100px base
                _timeZoneService,
                _weatherDataService,
                _recommendationInfoService,
                _calculatorDataService,
                _settingsFacade,
                "preview_" + id);

            control.IsHitTestVisible = false;
            
            return new Viewbox
            {
                Child = control,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(12)
            };
        }
        catch (Exception ex)
        {
            return new TextBlock { Text = "无法预览", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
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

public record LibraryCategoryItem(string Id, string DisplayName, string Icon);

public class LibraryComponentItem
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public Control? PreviewContent { get; set; }
    public bool HasPreview => PreviewContent != null;
}

