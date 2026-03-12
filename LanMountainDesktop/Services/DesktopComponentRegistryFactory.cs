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
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Services;

public static class DesktopComponentRegistryFactory
{
    public static ComponentRegistry Create(PluginRuntimeService? pluginRuntimeService)
    {
        var registry = ComponentRegistry
            .CreateDefault()
            .RegisterExtensions(
                JsonComponentExtensionProvider.LoadProvidersFromDirectory(
                    Path.Combine(AppContext.BaseDirectory, "Extensions", "Components")));

        var pluginDefinitions = GetPluginDefinitions(registry, pluginRuntimeService);
        return pluginDefinitions.Count == 0
            ? registry
            : registry.RegisterComponents(pluginDefinitions);
    }

    public static DesktopComponentRuntimeRegistry CreateRuntimeRegistry(
        ComponentRegistry componentRegistry,
        PluginRuntimeService? pluginRuntimeService)
    {
        var registrations = DesktopComponentRuntimeRegistry.GetDefaultRegistrations().ToList();
        var registeredIds = new HashSet<string>(
            registrations.Select(registration => registration.ComponentId),
            StringComparer.OrdinalIgnoreCase);

        if (pluginRuntimeService is not null)
        {
            foreach (var contribution in pluginRuntimeService.DesktopComponents)
            {
                var registration = contribution.Registration;
                if (!componentRegistry.TryGetDefinition(registration.ComponentId, out _))
                {
                    continue;
                }

                if (!registeredIds.Add(registration.ComponentId))
                {
                    Debug.WriteLine(
                        $"[PluginRuntime] Skipped plugin widget '{registration.ComponentId}' from '{contribution.Plugin.Manifest.Id}' because a runtime registration already exists.");
                    continue;
                }

                registrations.Add(new DesktopComponentRuntimeRegistration(
                    registration.ComponentId,
                    registration.DisplayNameLocalizationKey,
                    factoryContext => CreatePluginControl(contribution, factoryContext),
                    registration.CornerRadiusResolver));
            }
        }

        return new DesktopComponentRuntimeRegistry(componentRegistry, registrations);
    }

    private static List<DesktopComponentDefinition> GetPluginDefinitions(
        ComponentRegistry baseRegistry,
        PluginRuntimeService? pluginRuntimeService)
    {
        var definitions = new List<DesktopComponentDefinition>();
        if (pluginRuntimeService is null)
        {
            return definitions;
        }

        var knownIds = new HashSet<string>(
            baseRegistry.GetAll().Select(definition => definition.Id),
            StringComparer.OrdinalIgnoreCase);

        foreach (var contribution in pluginRuntimeService.DesktopComponents)
        {
            var registration = contribution.Registration;
            if (!knownIds.Add(registration.ComponentId))
            {
                Debug.WriteLine(
                    $"[PluginRuntime] Skipped plugin widget '{registration.ComponentId}' from '{contribution.Plugin.Manifest.Id}' because the component id already exists.");
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
                registration.ResizeMode == PluginDesktopComponentResizeMode.Free
                    ? DesktopComponentResizeMode.Free
                    : DesktopComponentResizeMode.Proportional));
        }

        return definitions;
    }

    private static Control CreatePluginControl(
        PluginDesktopComponentContribution contribution,
        DesktopComponentControlFactoryContext context)
    {
        try
        {
            var pluginContext = new PluginDesktopComponentContext(
                contribution.Plugin.Manifest,
                contribution.Plugin.Context.PluginDirectory,
                contribution.Plugin.Context.DataDirectory,
                contribution.Plugin.Services,
                contribution.Plugin.Context.Properties,
                contribution.Registration.ComponentId,
                context.PlacementId,
                context.CellSize);

            return contribution.Registration.ControlFactory(contribution.Plugin.Services, pluginContext);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[PluginRuntime] Failed to create widget '{contribution.Registration.ComponentId}' from '{contribution.Plugin.Manifest.Id}': {ex}");
            return CreatePluginErrorControl(contribution, ex);
        }
    }

    private static Control CreatePluginErrorControl(
        PluginDesktopComponentContribution contribution,
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
                        Text = $"Plugin {contribution.Plugin.Manifest.Name} failed to create this widget.",
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
