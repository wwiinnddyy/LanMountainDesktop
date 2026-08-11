using LanMountainDesktop.Services.PluginMarket;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PluginMarketCompatibilityTests
{
    [Fact]
    public void Validate_RejectsPluginFromPreviousApiMajor()
    {
        var error = AirAppMarketCompatibility.Validate(
            CreatePlugin(apiVersion: "4.0.0"),
            new Version(0, 8, 8),
            "5.0.0");

        Assert.NotNull(error);
        Assert.Contains("incompatible API version 4.0.0", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ChecksApiMajorEvenWhenHostProductVersionIsUnavailable()
    {
        var error = AirAppMarketCompatibility.Validate(
            CreatePlugin(apiVersion: "4.0.0"),
            hostVersion: null,
            hostApiVersion: "5.0.0");

        Assert.NotNull(error);
        Assert.Contains("Host API version is 5.0.0", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsPluginThatRequiresNewerHost()
    {
        var error = AirAppMarketCompatibility.Validate(
            CreatePlugin(apiVersion: "5.0.0", minHostVersion: "0.9.0"),
            new Version(0, 8, 8),
            "5.0.0");

        Assert.NotNull(error);
        Assert.Contains("requires host version 0.9.0 or newer", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsCompatiblePlugin()
    {
        var error = AirAppMarketCompatibility.Validate(
            CreatePlugin(apiVersion: "5.2.0", minHostVersion: "0.8.0"),
            new Version(0, 8, 8),
            "5.0.0");

        Assert.Null(error);
    }

    private static AirAppMarketPluginEntry CreatePlugin(
        string apiVersion,
        string minHostVersion = "0.0.1") =>
        new()
        {
            PluginId = "Example.Plugin",
            Id = "Example.Plugin",
            Name = "Example Plugin",
            Description = "Compatibility test plugin.",
            Author = "LanMountainDesktop",
            Version = "1.0.0",
            ApiVersion = apiVersion,
            MinHostVersion = minHostVersion,
            RepositoryUrl = "https://github.com/example/example-plugin",
            PackageSources =
            [
                new AirAppMarketPluginPackageSourceEntry
                {
                    Kind = "workspaceLocal",
                    Url = "workspace://Example.Plugin/Example.Plugin.1.0.0.laapp",
                    SourceKind = PluginPackageSourceKind.WorkspaceLocal
                }
            ]
        };
}
