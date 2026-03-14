using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Plugins;
using LanMountainDesktop.Services;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views.SettingsPages;
using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.Services.Settings;

public sealed class SettingsPageDescriptor
{
    private readonly Func<ISettingsPageHostContext, Control> _factory;

    public SettingsPageDescriptor(
        string pageId,
        string title,
        string? description,
        string iconKey,
        string? selectedIconKey,
        SettingsPageCategory category,
        int sortOrder,
        string? pluginId,
        bool isBuiltIn,
        bool hideDefault,
        bool hidePageTitle,
        bool useFullWidth,
        string? groupId,
        Func<ISettingsPageHostContext, Control> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconKey);
        ArgumentNullException.ThrowIfNull(factory);

        PageId = pageId.Trim();
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IconKey = iconKey.Trim();
        SelectedIconKey = string.IsNullOrWhiteSpace(selectedIconKey) ? IconKey : selectedIconKey.Trim();
        Category = category;
        SortOrder = sortOrder;
        PluginId = string.IsNullOrWhiteSpace(pluginId) ? null : pluginId.Trim();
        IsBuiltIn = isBuiltIn;
        HideDefault = hideDefault;
        HidePageTitle = hidePageTitle;
        UseFullWidth = useFullWidth;
        GroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        _factory = factory;
    }

    public string PageId { get; }

    public string Title { get; }

    public string? Description { get; }

    public string IconKey { get; }

    public string SelectedIconKey { get; }

    public SettingsPageCategory Category { get; }

    public int SortOrder { get; }

    public string? PluginId { get; }

    public bool IsBuiltIn { get; }

    public bool HideDefault { get; }

    public bool HidePageTitle { get; }

    public bool UseFullWidth { get; }

    public string? GroupId { get; }

    public Control CreatePage(ISettingsPageHostContext hostContext) => _factory(hostContext);
}

public interface ISettingsPageRegistry
{
    void Rebuild();

    IReadOnlyList<SettingsPageDescriptor> GetPages();

    bool TryGetPage(string pageId, out SettingsPageDescriptor? descriptor);
}

internal sealed class SettingsPageRegistry : ISettingsPageRegistry, IDisposable
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IHostApplicationLifecycle _hostApplicationLifecycle;
    private readonly LocalizationService _localizationService;
    private readonly Func<PluginRuntimeService?> _pluginRuntimeAccessor;
    private readonly object _gate = new();
    private readonly List<SettingsPageDescriptor> _pages = [];
    private ServiceProvider? _hostServices;

    public SettingsPageRegistry(
        ISettingsFacadeService settingsFacade,
        IHostApplicationLifecycle hostApplicationLifecycle,
        LocalizationService localizationService,
        Func<PluginRuntimeService?> pluginRuntimeAccessor)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _hostApplicationLifecycle = hostApplicationLifecycle ?? throw new ArgumentNullException(nameof(hostApplicationLifecycle));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _pluginRuntimeAccessor = pluginRuntimeAccessor ?? throw new ArgumentNullException(nameof(pluginRuntimeAccessor));
    }

    public void Rebuild()
    {
        lock (_gate)
        {
            _pages.Clear();
            RebuildHostServices();

            RegisterAssemblyPages(
                typeof(App).Assembly,
                _hostServices!,
                pluginId: null,
                isBuiltIn: true);

            var pluginRuntime = _pluginRuntimeAccessor();
            if (pluginRuntime is null)
            {
                SortPages();
                return;
            }

            foreach (var loadedPlugin in pluginRuntime.LoadedPlugins)
            {
                RegisterPluginPages(loadedPlugin);
                RegisterLegacyPluginSections(loadedPlugin);
            }

            SortPages();
        }
    }

    public IReadOnlyList<SettingsPageDescriptor> GetPages()
    {
        lock (_gate)
        {
            return _pages.ToArray();
        }
    }

    public bool TryGetPage(string pageId, out SettingsPageDescriptor? descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        lock (_gate)
        {
            descriptor = _pages.FirstOrDefault(item =>
                string.Equals(item.PageId, pageId, StringComparison.OrdinalIgnoreCase));
            return descriptor is not null;
        }
    }

    public void Dispose()
    {
        _hostServices?.Dispose();
    }

    private void RebuildHostServices()
    {
        _hostServices?.Dispose();

        var services = new ServiceCollection();
        services.AddSingleton(_settingsFacade);
        services.AddSingleton(_settingsFacade.Settings);
        services.AddSingleton(_settingsFacade.Catalog);
        services.AddSingleton(_hostApplicationLifecycle);
        services.AddSingleton(_localizationService);
        services.AddSingleton<ILocationService>(_ => HostLocationServiceProvider.GetOrCreate());
        services.AddSingleton<WeatherLocationRefreshService>();

        var pluginRuntime = _pluginRuntimeAccessor();
        if (pluginRuntime is not null)
        {
            services.AddSingleton(pluginRuntime);
        }

        _hostServices = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = false,
            ValidateOnBuild = false
        });
    }

    private void RegisterAssemblyPages(
        Assembly assembly,
        IServiceProvider services,
        string? pluginId,
        bool isBuiltIn)
    {
        foreach (var pageType in assembly.GetTypes()
                     .Where(type => !type.IsAbstract && typeof(SettingsPageBase).IsAssignableFrom(type)))
        {
            var pageInfo = pageType.GetCustomAttribute<SettingsPageInfoAttribute>();
            if (pageInfo is null)
            {
                continue;
            }

            var category = isBuiltIn ? pageInfo.Category : SettingsPageCategory.Plugins;
            var sortOrder = isBuiltIn ? pageInfo.SortOrder : 100 + pageInfo.SortOrder;
            var title = ResolveLocalizedText(pageInfo.TitleLocalizationKey, pageInfo.Name);
            var description = ResolveLocalizedText(pageInfo.DescriptionLocalizationKey, null);
            _pages.Add(new SettingsPageDescriptor(
                pageInfo.Id,
                title,
                description,
                pageInfo.IconKey,
                pageInfo.SelectedIconKey,
                category,
                sortOrder,
                pluginId,
                isBuiltIn,
                pageInfo.HideDefault,
                pageInfo.HidePageTitle,
                pageInfo.UseFullWidth,
                pageInfo.GroupId,
                hostContext => CreatePage(services, pageType, hostContext)));
        }
    }

    private void RegisterPluginPages(LoadedPlugin loadedPlugin)
    {
        RegisterAssemblyPages(
            loadedPlugin.Assembly,
            loadedPlugin.Services,
            loadedPlugin.Manifest.Id,
            isBuiltIn: false);
    }

    private void RegisterLegacyPluginSections(LoadedPlugin loadedPlugin)
    {
        var localizer = PluginLocalizer.Create(loadedPlugin.RuntimeContext);

        foreach (var section in loadedPlugin.SettingsSections)
        {
            var pageId = $"plugin:{loadedPlugin.Manifest.Id}:{section.Id}";
            var title = localizer.GetString(section.TitleLocalizationKey, section.TitleLocalizationKey);
            var description = string.IsNullOrWhiteSpace(section.DescriptionLocalizationKey)
                ? null
                : localizer.GetString(section.DescriptionLocalizationKey, section.DescriptionLocalizationKey);

            _pages.Add(new SettingsPageDescriptor(
                pageId,
                title,
                description,
                section.IconKey,
                section.IconKey,
                SettingsPageCategory.Plugins,
                200 + section.SortOrder,
                loadedPlugin.Manifest.Id,
                isBuiltIn: false,
                hideDefault: false,
                hidePageTitle: false,
                useFullWidth: false,
                groupId: null,
                hostContext =>
                {
                    var page = new GeneratedPluginSettingsPage(
                        new PluginGeneratedSettingsPageViewModel(
                            _settingsFacade.Settings,
                            loadedPlugin.Manifest.Id,
                            section,
                            localizer));
                    page.InitializeHostContext(hostContext);
                    return page;
                }));
        }
    }

    private void SortPages()
    {
        _pages.Sort(static (left, right) =>
        {
            var categoryCompare = left.Category.CompareTo(right.Category);
            if (categoryCompare != 0)
            {
                return categoryCompare;
            }

            var sortOrderCompare = left.SortOrder.CompareTo(right.SortOrder);
            if (sortOrderCompare != 0)
            {
                return sortOrderCompare;
            }

            var pluginCompare = string.Compare(left.PluginId, right.PluginId, StringComparison.OrdinalIgnoreCase);
            if (pluginCompare != 0)
            {
                return pluginCompare;
            }

            return string.Compare(left.PageId, right.PageId, StringComparison.OrdinalIgnoreCase);
        });
    }

    private string ResolveLocalizedText(string? localizationKey, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(localizationKey))
        {
            return fallback ?? string.Empty;
        }

        var languageCode = _settingsFacade.Region.Get().LanguageCode;
        var normalizedLanguageCode = _localizationService.NormalizeLanguageCode(languageCode);
        return _localizationService.GetString(
            normalizedLanguageCode,
            localizationKey,
            string.IsNullOrWhiteSpace(fallback) ? localizationKey : fallback);
    }

    private static Control CreatePage(
        IServiceProvider services,
        Type pageType,
        ISettingsPageHostContext hostContext)
    {
        var page = (Control)ActivatorUtilities.CreateInstance(services, pageType);
        if (page is SettingsPageBase settingsPage)
        {
            settingsPage.InitializeHostContext(hostContext);
        }

        return page;
    }
}
