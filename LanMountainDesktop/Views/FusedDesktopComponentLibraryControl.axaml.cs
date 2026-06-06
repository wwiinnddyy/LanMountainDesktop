using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views;

public partial class FusedDesktopComponentLibraryControl : UserControl
{
    public event EventHandler<string>? AddComponentRequested;

    private const double PreviewSwipeThreshold = 48d;

    private static readonly LocalizationService LocalizationService = new();

    private readonly ComponentLibraryWindowViewModel _viewModel = new();
    private readonly ISettingsFacadeService _settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
    private readonly IWeatherInfoService _weatherDataService;
    private readonly TimeZoneService _timeZoneService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();

    private List<DesktopComponentDefinition> _allDefinitions = new();
    private IReadOnlyList<DesktopComponentDefinition> _selectedCategoryDefinitions = [];
    private int _selectedComponentIndex;
    private ComponentRegistry? _componentRegistry;
    private DesktopComponentRuntimeRegistry? _componentRuntimeRegistry;
    private Control? _selectedPreviewControl;
    private DesktopComponentDefinition? _selectedPreviewDefinition;
    private FusedDesktopLibraryPreviewMetrics? _selectedPreviewMetrics;
    private bool _isPreviewSwipeActive;
    private Point _previewSwipeStartPoint;

    public FusedDesktopComponentLibraryControl()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _weatherDataService = _settingsFacade.Weather.GetWeatherInfoService();
        _timeZoneService = _settingsFacade.Region.GetTimeZoneService();

        ApplyLocalization();
        LoadRegistry();
        LoadCategories();

        CategoryListBox.ContainerPrepared += OnCategoryListBoxContainerPrepared;
        if (_viewModel.Categories.Count > 0)
        {
            CategoryListBox.SelectedIndex = 0;
        }
    }

    private void ApplyLocalization()
    {
        var languageCode = _settingsFacade.Region.Get().LanguageCode;
        _viewModel.Title = L(languageCode, "fused_desktop.library.title", "Add widgets");
        FindMoreComponentsTextBlock.Text = L(
            languageCode,
            "fused_desktop.library.find_more",
            "Find more widgets");
        AddComponentButtonTextBlock.Text = L(
            languageCode,
            "fused_desktop.library.add_button",
            "Add widget");
        EmptySelectionTextBlock.Text = L(
            languageCode,
            "fused_desktop.library.empty_selection",
            "Choose a category to view widgets.");
    }

    private void OnCategoryListBoxContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        _ = sender;
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
            .Where(static definition => definition.AllowDesktopPlacement)
            .ToList();
    }

    private void LoadCategories()
    {
        _viewModel.Categories.Clear();

        var languageCode = _settingsFacade.Region.Get().LanguageCode;
        _viewModel.Categories.Add(new ComponentLibraryCategoryViewModel(
            "all",
            L(languageCode, "component_category.all", "All"),
            Icon.Apps,
            Array.Empty<ComponentLibraryItemViewModel>()));

        var usedCategories = _allDefinitions
            .Select(static definition => definition.Category)
            .Where(static category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var category in usedCategories)
        {
            var categoryComponents = _allDefinitions
                .Where(definition => string.Equals(definition.Category, category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(definition => CreateComponentItem(definition, languageCode))
                .ToArray();

            var categoryDefinitions = _allDefinitions
                .Where(definition => string.Equals(definition.Category, category, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _viewModel.Categories.Add(new ComponentLibraryCategoryViewModel(
                category,
                GetLocalizedCategoryTitle(languageCode, category),
                ComponentCategoryIconResolver.ResolveCategoryIcon(category, categoryDefinitions),
                categoryComponents));
        }
    }

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
        return LocalizationService.GetString(languageCode, key, fallback);
    }

    private ComponentLibraryItemViewModel CreateComponentItem(DesktopComponentDefinition definition, string languageCode)
    {
        return new ComponentLibraryItemViewModel(
            definition.Id,
            ResolveComponentDisplayName(definition, languageCode),
            ResolveComponentDescription(definition, languageCode));
    }

    private string ResolveComponentDisplayName(DesktopComponentDefinition definition, string languageCode)
    {
        if (_componentRuntimeRegistry is not null &&
            _componentRuntimeRegistry.TryGetDescriptor(definition.Id, out var descriptor) &&
            !string.IsNullOrWhiteSpace(descriptor.DisplayNameLocalizationKey))
        {
            return L(languageCode, descriptor.DisplayNameLocalizationKey, definition.DisplayName);
        }

        return definition.DisplayName;
    }

    private string ResolveComponentDescription(DesktopComponentDefinition definition, string languageCode)
    {
        if (_componentRuntimeRegistry is not null &&
            _componentRuntimeRegistry.TryGetDescriptor(definition.Id, out var descriptor))
        {
            if (!string.IsNullOrWhiteSpace(descriptor.DescriptionLocalizationKey))
            {
                return L(
                    languageCode,
                    descriptor.DescriptionLocalizationKey,
                    descriptor.Description ?? CreateComponentFallbackDescription(definition, languageCode));
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Description))
            {
                return descriptor.Description;
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.DescriptionLocalizationKey))
        {
            return L(
                languageCode,
                definition.DescriptionLocalizationKey,
                definition.Description ?? CreateComponentFallbackDescription(definition, languageCode));
        }

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            return definition.Description;
        }

        return CreateComponentFallbackDescription(definition, languageCode);
    }

    private string CreateComponentFallbackDescription(DesktopComponentDefinition definition, string languageCode)
    {
        var categoryTitle = GetLocalizedCategoryTitle(languageCode, definition.Category);
        var fallbackFormat = L(
            languageCode,
            "fused_desktop.library.component_summary_format",
            "{0} - {1} x {2}");
        return string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            fallbackFormat,
            categoryTitle,
            Math.Max(1, definition.MinWidthCells),
            Math.Max(1, definition.MinHeightCells));
    }

    private void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateSelectedComponent();
    }

    private void UpdateSelectedComponent()
    {
        if (CategoryListBox.SelectedItem is not ComponentLibraryCategoryViewModel selectedCategory)
        {
            _viewModel.SelectedComponent = null;
            _selectedPreviewDefinition = null;
            _selectedPreviewMetrics = null;
            SetSelectedPreviewControl(null);
            return;
        }

        var filtered = selectedCategory.Id == "all"
            ? _allDefinitions.OrderBy(static definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            : _allDefinitions
                .Where(definition => string.Equals(definition.Category, selectedCategory.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase);

        _selectedCategoryDefinitions = filtered.ToList();
        _selectedComponentIndex = 0;
        ApplySelectedComponentIndex();
    }

    private void ApplySelectedComponentIndex()
    {
        if (_selectedCategoryDefinitions.Count == 0)
        {
            _viewModel.SelectedComponent = null;
            _selectedPreviewDefinition = null;
            _selectedPreviewMetrics = null;
            SetSelectedPreviewControl(null);
            return;
        }

        _selectedComponentIndex = NormalizeComponentIndex(_selectedComponentIndex);
        var selectedDefinition = _selectedCategoryDefinitions[_selectedComponentIndex];
        _selectedPreviewDefinition = selectedDefinition;
        _selectedPreviewMetrics = null;
        _viewModel.SelectedComponent = CreateComponentItem(selectedDefinition, _settingsFacade.Region.Get().LanguageCode);
        RefreshSelectedPreviewControl(force: true);
    }

    private int NormalizeComponentIndex(int index)
    {
        if (_selectedCategoryDefinitions.Count == 0)
        {
            return 0;
        }

        var count = _selectedCategoryDefinitions.Count;
        return ((index % count) + count) % count;
    }

    private void MoveSelectedComponent(int direction)
    {
        if (_selectedCategoryDefinitions.Count <= 1 || direction == 0)
        {
            return;
        }

        _selectedComponentIndex = NormalizeComponentIndex(_selectedComponentIndex + direction);
        ApplySelectedComponentIndex();
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPreviewSwipeActive = true;
            _previewSwipeStartPoint = e.GetPosition(this);
            PreviewInteractionHost.Focus();
            e.Pointer.Capture(PreviewInteractionHost);
        }
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _ = sender;
        if (!_isPreviewSwipeActive)
        {
            return;
        }

        _isPreviewSwipeActive = false;
        e.Pointer.Capture(null);

        var endPoint = e.GetPosition(this);
        var delta = endPoint - _previewSwipeStartPoint;
        if (Math.Abs(delta.Y) < PreviewSwipeThreshold || Math.Abs(delta.Y) <= Math.Abs(delta.X))
        {
            return;
        }

        MoveSelectedComponent(delta.Y < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void OnPreviewPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _ = sender;
        _ = e;
        _isPreviewSwipeActive = false;
    }

    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _ = sender;
        if (Math.Abs(e.Delta.Y) <= 0)
        {
            return;
        }

        MoveSelectedComponent(e.Delta.Y < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key == Key.Down)
        {
            MoveSelectedComponent(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveSelectedComponent(-1);
            e.Handled = true;
        }
    }

    private void OnPreviewInteractionHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshSelectedPreviewControl(force: false);
    }

    private void RefreshSelectedPreviewControl(bool force)
    {
        if (_selectedPreviewDefinition is null)
        {
            _selectedPreviewMetrics = null;
            SetSelectedPreviewControl(null);
            return;
        }

        var metrics = FusedDesktopLibraryPreviewLayout.Calculate(
            _selectedPreviewDefinition,
            PreviewInteractionHost.Bounds.Size);
        if (!force &&
            _selectedPreviewMetrics is { } previousMetrics &&
            ArePreviewMetricsClose(previousMetrics, metrics))
        {
            return;
        }

        _selectedPreviewMetrics = metrics;
        if (!force && _selectedPreviewControl is not null)
        {
            ApplyPreviewMetricsToControl(_selectedPreviewControl, metrics);
            return;
        }

        SetSelectedPreviewControl(CreateStaticPreviewControl(_selectedPreviewDefinition, metrics));
    }

    private Control? CreateStaticPreviewControl(
        DesktopComponentDefinition definition,
        FusedDesktopLibraryPreviewMetrics metrics)
    {
        if (_componentRuntimeRegistry is null ||
            !_componentRuntimeRegistry.TryGetDescriptor(definition.Id, out var descriptor))
        {
            return null;
        }

        try
        {
            var control = descriptor.CreateControl(
                metrics.CellSize,
                _timeZoneService,
                _weatherDataService,
                _recommendationInfoService,
                _calculatorDataService,
                _settingsFacade,
                placementId: null,
                renderMode: DesktopComponentRenderMode.LibraryPreview);
            ApplyPreviewMetricsToControl(control, metrics);
            ComponentPreviewRuntimeQuiescer.Attach(control);
            return control;
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            AppLogger.Warn(
                "ComponentLibrary",
                $"Failed to create static fused preview for component '{definition.Id}'.",
                ex);
            return null;
        }
    }

    private static void ApplyPreviewMetricsToControl(
        Control control,
        FusedDesktopLibraryPreviewMetrics metrics)
    {
        control.Width = metrics.Width;
        control.Height = metrics.Height;
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        if (control is IDesktopComponentWidget sizedComponent)
        {
            sizedComponent.ApplyCellSize(metrics.CellSize);
        }
    }

    private static bool ArePreviewMetricsClose(
        FusedDesktopLibraryPreviewMetrics first,
        FusedDesktopLibraryPreviewMetrics second)
    {
        const double tolerance = 0.5d;
        return first.WidthCells == second.WidthCells &&
               first.HeightCells == second.HeightCells &&
               Math.Abs(first.CellSize - second.CellSize) <= tolerance &&
               Math.Abs(first.Width - second.Width) <= tolerance &&
               Math.Abs(first.Height - second.Height) <= tolerance;
    }

    private void SetSelectedPreviewControl(Control? control)
    {
        DisposeSelectedPreviewControl();
        _selectedPreviewControl = control;
        if (SelectedComponentPreviewHost is not null)
        {
            SelectedComponentPreviewHost.Content = control;
        }
    }

    private void DisposeSelectedPreviewControl()
    {
        if (_selectedPreviewControl is null)
        {
            return;
        }

        ComponentPreviewRuntimeQuiescer.Detach(_selectedPreviewControl);
        if (_selectedPreviewControl is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _selectedPreviewControl = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeSelectedPreviewControl();
        base.OnDetachedFromVisualTree(e);
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
        if (Application.Current is App app)
        {
            app.OpenIndependentSettingsModule("FusedDesktopComponentLibrary", "plugin-catalog");
        }

        this.FindAncestorOfType<Window>()?.Close();
    }
}
