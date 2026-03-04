using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LanMountainDesktop.ComponentSystem.Extensions;

public sealed class JsonComponentExtensionProvider : IComponentExtensionProvider
{
    private readonly IReadOnlyList<DesktopComponentDefinition> _definitions;

    private JsonComponentExtensionProvider(IReadOnlyList<DesktopComponentDefinition> definitions)
    {
        _definitions = definitions;
    }

    public IReadOnlyList<DesktopComponentDefinition> GetComponents()
    {
        return _definitions;
    }

    public static IReadOnlyList<IComponentExtensionProvider> LoadProvidersFromDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return Array.Empty<IComponentExtensionProvider>();
        }

        var providers = new List<IComponentExtensionProvider>();
        foreach (var filePath in Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            var provider = TryLoadFromFile(filePath);
            if (provider is not null)
            {
                providers.Add(provider);
            }
        }

        return providers;
    }

    private static JsonComponentExtensionProvider? TryLoadFromFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var entries = JsonSerializer.Deserialize<List<ComponentExtensionEntry>>(json);
            if (entries is null || entries.Count == 0)
            {
                return null;
            }

            var definitions = new List<DesktopComponentDefinition>();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) ||
                    string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    continue;
                }

                definitions.Add(new DesktopComponentDefinition(
                    entry.Id.Trim(),
                    entry.DisplayName.Trim(),
                    string.IsNullOrWhiteSpace(entry.IconKey) ? "PuzzlePiece" : entry.IconKey,
                    string.IsNullOrWhiteSpace(entry.Category) ? "Extensions" : entry.Category,
                    MinWidthCells: Math.Max(1, entry.MinWidthCells),
                    MinHeightCells: Math.Max(1, entry.MinHeightCells),
                    AllowStatusBarPlacement: entry.AllowStatusBarPlacement,
                    AllowDesktopPlacement: entry.AllowDesktopPlacement));
            }

            return definitions.Count == 0 ? null : new JsonComponentExtensionProvider(definitions);
        }
        catch
        {
            return null;
        }
    }

    private sealed class ComponentExtensionEntry
    {
        public string Id { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string IconKey { get; set; } = string.Empty;

        public string Category { get; set; } = "Extensions";

        public int MinWidthCells { get; set; } = 1;

        public int MinHeightCells { get; set; } = 1;

        public bool AllowStatusBarPlacement { get; set; }

        public bool AllowDesktopPlacement { get; set; } = true;
    }
}
