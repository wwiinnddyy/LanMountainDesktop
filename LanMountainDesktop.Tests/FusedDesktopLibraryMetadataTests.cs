using System.Text.Json;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.ComponentSystem.Extensions;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class FusedDesktopLibraryMetadataTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(FusedDesktopLibraryMetadataTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void PluginDesktopComponentDescriptionMetadata_ReachesRuntimeDescriptor()
    {
        const string componentId = "plugin.metadata.widget";
        var registration = new PluginDesktopComponentRegistration(
            _ => new Border(),
            new PluginDesktopComponentOptions
            {
                ComponentId = componentId,
                DisplayName = "Metadata Widget",
                IconKey = "Apps",
                Category = "Plugins",
                Description = "Plugin supplied description.",
                DescriptionLocalizationKey = "plugin.metadata.description"
            });

        Assert.Equal("Plugin supplied description.", registration.Description);
        Assert.Equal("plugin.metadata.description", registration.DescriptionLocalizationKey);

        var registry = ComponentRegistry.CreateDefault().RegisterComponents(
        [
            new DesktopComponentDefinition(
                registration.ComponentId,
                registration.DisplayName,
                registration.IconKey,
                registration.Category,
                registration.MinWidthCells,
                registration.MinHeightCells,
                registration.AllowStatusBarPlacement,
                registration.AllowDesktopPlacement,
                Description: registration.Description,
                DescriptionLocalizationKey: registration.DescriptionLocalizationKey)
        ]);
        var runtimeRegistry = new DesktopComponentRuntimeRegistry(
            registry,
            [
                new DesktopComponentRuntimeRegistration(
                    componentId,
                    displayNameLocalizationKey: registration.DisplayNameLocalizationKey,
                    _ => new Border(),
                    cornerRadiusResolver: (Func<double, double>?)null)
            ]);

        Assert.True(runtimeRegistry.TryGetDescriptor(componentId, out var descriptor));
        Assert.Equal("Plugin supplied description.", descriptor.Description);
        Assert.Equal("plugin.metadata.description", descriptor.DescriptionLocalizationKey);
    }

    [Fact]
    public void JsonComponentExtensionProvider_LoadsOptionalDescriptionMetadata()
    {
        var extensionDirectory = Path.Combine(_tempRoot, "extensions");
        Directory.CreateDirectory(extensionDirectory);
        File.WriteAllText(
            Path.Combine(extensionDirectory, "components.json"),
            """
            [
              {
                "id": "json.description.widget",
                "displayName": "JSON Description Widget",
                "iconKey": "Apps",
                "category": "Extensions",
                "description": "Description from JSON.",
                "descriptionLocalizationKey": "json.description.widget.description"
              },
              {
                "id": "json.default.widget",
                "displayName": "JSON Default Widget",
                "description": "   ",
                "descriptionLocalizationKey": "   "
              }
            ]
            """);

        var definitions = JsonComponentExtensionProvider
            .LoadProvidersFromDirectory(extensionDirectory)
            .SelectMany(provider => provider.GetComponents())
            .ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);

        var described = definitions["json.description.widget"];
        Assert.Equal("Description from JSON.", described.Description);
        Assert.Equal("json.description.widget.description", described.DescriptionLocalizationKey);

        var defaults = definitions["json.default.widget"];
        Assert.Null(defaults.Description);
        Assert.Null(defaults.DescriptionLocalizationKey);
    }

    [Fact]
    public void FusedDesktopLibraryLocalizationFiles_ContainRequiredKeys()
    {
        var requiredKeys = new[]
        {
            "fused_desktop.library.title",
            "fused_desktop.library.add_button",
            "fused_desktop.library.find_more",
            "fused_desktop.library.empty_selection",
            "fused_desktop.library.component_summary_format"
        };

        foreach (var language in new[] { "zh-CN", "en-US", "ja-JP", "ko-KR" })
        {
            var json = ReadRepositoryFile("LanMountainDesktop", "Localization", $"{language}.json");
            var table = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            Assert.NotNull(table);
            foreach (var key in requiredKeys)
            {
                Assert.True(table!.ContainsKey(key), $"{language} is missing {key}.");
                Assert.False(string.IsNullOrWhiteSpace(table[key]), $"{language} has an empty {key}.");
            }
        }
    }

    [Fact]
    public void FusedDesktopLibraryLifecycle_KeepsAddOpenAndUsesEditModeBoundary()
    {
        var appSource = ReadRepositoryFile("LanMountainDesktop", "App.axaml.cs");
        var openSource = ExtractMethodSource(appSource, "OpenFusedDesktopComponentLibraryFromUi");
        var closedSource = ExtractMethodSource(appSource, "OnFusedComponentLibraryWindowClosed");
        var librarySource = ReadRepositoryFile("LanMountainDesktop", "Views", "FusedDesktopComponentLibraryWindow.axaml.cs");
        var addSource = ExtractMethodSource(librarySource, "OnAddComponentRequested");

        Assert.Contains("EnterEditMode()", openSource);
        Assert.Contains("ExitEditMode()", closedSource);
        Assert.Contains("AddComponent(componentId, this)", addSource);
        Assert.DoesNotContain("Close()", addSource);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            if (File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                break;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }

    private static string ExtractMethodSource(string source, string methodName)
    {
        var methodIndex = source.IndexOf($"private void {methodName}(", StringComparison.Ordinal);
        if (methodIndex < 0)
        {
            methodIndex = source.IndexOf($"private bool {methodName}(", StringComparison.Ordinal);
        }

        Assert.True(methodIndex >= 0, $"Could not locate method '{methodName}'.");

        var braceIndex = source.IndexOf('{', methodIndex);
        Assert.True(braceIndex >= 0, $"Could not locate method body for '{methodName}'.");

        var depth = 0;
        for (var i = braceIndex; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(methodIndex, i - methodIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method '{methodName}'.");
    }
}
