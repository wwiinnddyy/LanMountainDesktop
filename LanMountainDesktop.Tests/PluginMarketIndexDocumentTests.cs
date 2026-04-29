using LanMountainDesktop.Services.PluginMarket;
using System.Net;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PluginMarketIndexDocumentTests
{
    [Fact]
    public void Load_WithNestedV2Entry_MapsDisplayFieldsAndWorkspacePath()
    {
        var document = AirAppMarketIndexDocument.Load(CreateNestedIndexJson(), "test-index.json");
        var plugin = Assert.Single(document.Plugins);
        var source = Assert.Single(plugin.PackageSources);

        Assert.Equal("LanMountainDesktop.SamplePlugin", plugin.Id);
        Assert.Equal("LanMountain Sample Plugin", plugin.Name);
        Assert.Equal("SDK v5 sample plugin.", plugin.Description);
        Assert.Equal("LanMountainDesktop", plugin.Author);
        Assert.Equal("0.4.0", plugin.Version);
        Assert.Equal("5.0.0", plugin.ApiVersion);
        Assert.Equal("0.0.1", plugin.MinHostVersion);
        Assert.Equal("https://raw.githubusercontent.com/wwiinnddyy/LanAirApp/main/airappmarket/assets/sample-plugin.svg", plugin.IconUrl);
        Assert.Equal("https://raw.githubusercontent.com/wwiinnddyy/LanMountainDesktop.SamplePlugin/main/README.md", plugin.ReadmeUrl);
        Assert.Equal("workspace://LanMountainDesktop.SamplePlugin/LanMountainDesktop.SamplePlugin.0.4.0.laapp", source.Url);
        Assert.Equal(PluginPackageSourceKind.WorkspaceLocal, source.SourceKind);
    }

    [Fact]
    public async Task EnrichAsync_WhenRepositoryMetadataUnavailable_PreservesNestedDisplayFields()
    {
        var document = AirAppMarketIndexDocument.Load(
            CreateNestedIndexJson("LanMountainDesktop.MissingPlugin"),
            "test-index.json");
        using var httpClient = new HttpClient(new NotFoundHandler());
        using var resolver = new AirAppMarketMetadataResolverService(httpClient);

        var enriched = await resolver.EnrichAsync(document);
        var plugin = Assert.Single(enriched.Plugins);

        Assert.Equal("LanMountain Sample Plugin", plugin.Name);
        Assert.Equal("SDK v5 sample plugin.", plugin.Description);
        Assert.Equal("LanMountainDesktop", plugin.Author);
        Assert.Equal("0.4.0", plugin.Version);
        Assert.Equal("5.0.0", plugin.ApiVersion);
        Assert.Equal("0.0.1", plugin.MinHostVersion);
        Assert.Equal("v0.4.0", plugin.ReleaseTag);
        Assert.Equal("LanMountainDesktop.SamplePlugin.0.4.0.laapp", plugin.ReleaseAssetName);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", plugin.Sha256);
        Assert.Equal(1024, plugin.PackageSizeBytes);
    }

    private static string CreateNestedIndexJson(string repositoryName = "LanMountainDesktop.SamplePlugin")
    {
        return $$"""
            {
              "schemaVersion": "2.0.0",
              "sourceId": "official",
              "sourceName": "LanAirApp",
              "generatedAt": "2026-04-29T00:00:00Z",
              "contracts": [],
              "plugins": [
                {
                  "manifest": {
                    "id": "LanMountainDesktop.SamplePlugin",
                    "name": "LanMountain Sample Plugin",
                    "description": "SDK v5 sample plugin.",
                    "author": "LanMountainDesktop",
                    "version": "0.4.0",
                    "apiVersion": "5.0.0",
                    "entranceAssembly": "LanMountainDesktop.SamplePlugin.dll",
                    "sharedContracts": []
                  },
                  "compatibility": {
                    "minHostVersion": "0.0.1",
                    "apiVersion": "5.0.0"
                  },
                  "repository": {
                    "projectUrl": "https://github.com/wwiinnddyy/{{repositoryName}}",
                    "readmeUrl": "https://raw.githubusercontent.com/wwiinnddyy/{{repositoryName}}/main/README.md",
                    "homepageUrl": "https://github.com/wwiinnddyy/{{repositoryName}}",
                    "repositoryUrl": "https://github.com/wwiinnddyy/{{repositoryName}}",
                    "iconUrl": "https://raw.githubusercontent.com/wwiinnddyy/LanAirApp/main/airappmarket/assets/sample-plugin.svg",
                    "tags": [ "official", "sdk" ],
                    "releaseNotes": "Reference plugin for SDK v5 validation."
                  },
                  "publication": {
                    "releaseTag": "v0.4.0",
                    "releaseAssetName": "LanMountainDesktop.SamplePlugin.0.4.0.laapp",
                    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "packageSizeBytes": 1024,
                    "publishedAt": "2026-04-29T00:00:00Z",
                    "updatedAt": "2026-04-29T00:00:00Z",
                    "packageSources": [
                      {
                        "kind": "workspaceLocal",
                        "path": "workspace://LanMountainDesktop.SamplePlugin/LanMountainDesktop.SamplePlugin.0.4.0.laapp",
                        "assetName": "LanMountainDesktop.SamplePlugin.0.4.0.laapp",
                        "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        "sizeBytes": 1024,
                        "releaseTag": "v0.4.0",
                        "priority": 0
                      }
                    ]
                  },
                  "capabilities": {
                    "desktopComponents": [ "LanMountainDesktop.SamplePlugin.StatusClock" ],
                    "settingsSections": [ "status" ]
                  }
                }
              ]
            }
            """;
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
                Content = new StringContent("{}")
            });
        }
    }
}
