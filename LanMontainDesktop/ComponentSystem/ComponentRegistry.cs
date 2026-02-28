using System;
using System.Collections.Generic;
using System.Linq;
using LanMontainDesktop.ComponentSystem.Extensions;

namespace LanMontainDesktop.ComponentSystem;

public sealed class ComponentRegistry
{
    private readonly Dictionary<string, DesktopComponentDefinition> _definitions;

    public ComponentRegistry(IEnumerable<DesktopComponentDefinition> definitions)
    {
        _definitions = definitions
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public static ComponentRegistry CreateDefault()
    {
        var builtIn = new[]
        {
            new DesktopComponentDefinition(
                BuiltInComponentIds.Clock,
                "Clock",
                "Clock",
                "Status",
                MinWidthCells: 1,
                MinHeightCells: 1,
                AllowStatusBarPlacement: true,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.Blank2x4,
                "Blank 2x4",
                "Rectangle",
                "Layout",
                MinWidthCells: 2,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true)
        };

        return new ComponentRegistry(builtIn);
    }

    public ComponentRegistry RegisterExtensions(IEnumerable<IComponentExtensionProvider> providers)
    {
        var merged = _definitions.Values.ToList();
        foreach (var provider in providers)
        {
            var externalDefinitions = provider.GetComponents();
            if (externalDefinitions is null)
            {
                continue;
            }

            merged.AddRange(externalDefinitions);
        }

        return new ComponentRegistry(merged);
    }

    public bool TryGetDefinition(string componentId, out DesktopComponentDefinition definition)
    {
        return _definitions.TryGetValue(componentId, out definition!);
    }

    public bool IsKnownComponent(string componentId)
    {
        return _definitions.ContainsKey(componentId);
    }

    public bool AllowsStatusBarPlacement(string componentId)
    {
        return _definitions.TryGetValue(componentId, out var definition) && definition.AllowStatusBarPlacement;
    }

    public IReadOnlyList<DesktopComponentDefinition> GetAll()
    {
        return _definitions.Values.OrderBy(d => d.Category).ThenBy(d => d.DisplayName).ToList();
    }
}
