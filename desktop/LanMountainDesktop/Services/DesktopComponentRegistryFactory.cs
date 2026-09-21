using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.ComponentSystem.Extensions;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Services;

public static class DesktopComponentRegistryFactory
{
    public static ComponentRegistry Create(AirAppRuntimeService? airAppRuntimeService)
    {
        var registry = ComponentRegistry
            .CreateDefault()
            .RegisterExtensions(
                JsonComponentExtensionProvider.LoadProvidersFromDirectory(
                    Path.Combine(AppContext.BaseDirectory, "Extensions", "Components")));

        var airAppDefinitions = GetAirAppDefinitions(registry, airAppRuntimeService);
        return airAppDefinitions.Count == 0
            ? registry
            : registry.RegisterComponents(airAppDefinitions);
    }

    public static DesktopComponentRuntimeRegistry CreateRuntimeRegistry(
        ComponentRegistry componentRegistry,
        AirAppRuntimeService? airAppRuntimeService,
        ISettingsFacadeService settingsFacade,
        IMaterialColorService? materialColorService = null)
    {
        var registrations = DesktopComponentRuntimeRegistry.GetDefaultRegistrations().ToList();
        var registeredIds = new HashSet<string>(
            registrations.Select(registration => registration.ComponentId),
            StringComparer.OrdinalIgnoreCase);
        var resolvedMaterialColorService = materialColorService ?? HostMaterialColorProvider.GetOrCreate();

        if (airAppRuntimeService is not null)
        {
            foreach (var contribution in airAppRuntimeService.DesktopComponents)
            {
                var registration = contribution.Registration;
                if (!componentRegistry.TryGetDefinition(registration.ComponentId, out _))
                {
                    continue;
                }

                if (!registeredIds.Add(registration.ComponentId))
                {
                    Debug.WriteLine(
                        $"[AirAppRuntime] Skipped AirApp widget '{registration.ComponentId}' from '{contribution.AirApp.Manifest.Id}' because a runtime registration already exists.");
                    continue;
                }

                registrations.Add(new DesktopComponentRuntimeRegistration(
                    registration.ComponentId,
                    registration.DisplayNameLocalizationKey,
                    factoryContext => CreateAirAppControl(contribution, factoryContext, resolvedMaterialColorService),
                    chromeContext =>
                    {
                        var appearanceContext = CreateAirAppAppearanceContext(chromeContext);
                        return registration.ResolveCornerRadius(appearanceContext, chromeContext.CellSize);
                    }));
            }
        }

        _ = settingsFacade;
        return new DesktopComponentRuntimeRegistry(componentRegistry, registrations);
    }

    private static List<DesktopComponentDefinition> GetAirAppDefinitions(
        ComponentRegistry baseRegistry,
        AirAppRuntimeService? airAppRuntimeService)
    {
        var definitions = new List<DesktopComponentDefinition>();
        if (airAppRuntimeService is null)
        {
            return definitions;
        }

        var knownIds = new HashSet<string>(
            baseRegistry.GetAll().Select(definition => definition.Id),
            StringComparer.OrdinalIgnoreCase);

        foreach (var contribution in airAppRuntimeService.DesktopComponents)
        {
            var registration = contribution.Registration;
            if (!knownIds.Add(registration.ComponentId))
            {
                Debug.WriteLine(
                    $"[AirAppRuntime] Skipped AirApp widget '{registration.ComponentId}' from '{contribution.AirApp.Manifest.Id}' because the component id already exists.");
                continue;
            }

            definitions.Add(new DesktopComponentDefinition(
                registration.ComponentId,
                registration.DisplayName,
                registration.IconKey,
                registration.Category,
                registration.MinWidthCells,
                registration.MinHeightCells,
                registration.AllowStatusBarPlacement,
                registration.AllowDesktopPlacement,
                registration.ResizeMode == AirAppComponentResizeMode.Free
                    ? DesktopComponentResizeMode.Free
                    : DesktopComponentResizeMode.Proportional,
                Description: registration.Description,
                DescriptionLocalizationKey: registration.DescriptionLocalizationKey));
        }

        return definitions;
    }

    private static Control CreateAirAppControl(
        AirAppDesktopComponentContribution contribution,
        DesktopComponentControlFactoryContext context,
        IMaterialColorService materialColorService)
    {
        try
        {
            var settingsService = contribution.AirApp.Services.GetService(typeof(ISettingsService)) as ISettingsService
                ?? context.SettingsService;
            var airAppSettings = new AirAppScopedSettingsService(
                contribution.AirApp.Manifest.Id,
                settingsService);
            var airAppAppearance = new AirAppAppearanceContext(
                AirAppAppearanceSnapshotMapper.FromMaterialColorSnapshot(
                    materialColorService.GetMaterialColorSnapshot()));
            var airAppContext = new AirAppComponentContext(
                contribution.AirApp.Manifest,
                contribution.AirApp.Context.AirAppDirectory,
                contribution.AirApp.Context.DataDirectory,
                contribution.AirApp.Services,
                contribution.AirApp.Context.Properties,
                contribution.Registration.ComponentId,
                context.PlacementId,
                context.CellSize,
                airAppAppearance,
                airAppSettings)
            {
                OpenWindowHandler = windowId =>
                {
                    var launcher = AirAppLauncherServiceProvider.GetOrCreate();
                    launcher.OpenThirdPartyAirAppWindow(
                        contribution.AirApp.Manifest.Id,
                        windowId,
                        contribution.AirApp.Context.AirAppDirectory,
                        contribution.Registration.ComponentId,
                        context.PlacementId);
                    return Task.CompletedTask;
                }
            };

            return contribution.Registration.ControlFactory(contribution.AirApp.Services, airAppContext);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[AirAppRuntime] Failed to create widget '{contribution.Registration.ComponentId}' from '{contribution.AirApp.Manifest.Id}': {ex}");
            return CreateAirAppErrorControl(contribution, ex);
        }
    }

    private static IAirAppAppearanceContext CreateAirAppAppearanceContext(AirAppComponentChromeContext chromeContext)
    {
        return new AirAppAppearanceContext(new AirAppAppearanceSnapshot(
            CornerRadiusTokens: AirAppCornerRadiusTokens.FromShared(chromeContext.CornerRadiusTokens),
            ThemeVariant: "Unknown"));
    }

    private static Control CreateAirAppErrorControl(
        AirAppDesktopComponentContribution contribution,
        Exception exception)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#332B0F16")),
            BorderBrush = new SolidColorBrush(Color.Parse("#66F97316")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = contribution.Registration.DisplayName,
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = $"AirApp {contribution.AirApp.Manifest.Name} failed to create this widget.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = exception.Message,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }
}
