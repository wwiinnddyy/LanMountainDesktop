using LanMountainDesktop.Services.AirAppMarket;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AirAppMarketCompatibilityTests
{
    [Fact]
    public void Validate_RejectsAirAppFromPreviousApiMajor()
    {
        var error = AirAppMarketCompatibility.Validate(
            CreateAirApp(apiVersion: "4.0.0"),
            new Version(0, 8, 8),
            "5.0.0");

        Assert.NotNull(error);
        Assert.Contains("incompatible API version 4.0.0", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ChecksApiMajorEvenWhenHostProductVersionIsUnavailable()
    {
        var error = AirAppMarketCompatibility.Validate(
            CreateAirApp(apiVersion: "4.0.0"),
            hostVersion: null,
            hostApiVersion: "5.0.0");

        Assert.NotNull(error);
        Assert.Contains("Host API version is 5.0.0", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAirAppThatRequiresNewerHost()
    {
        var error = AirAppMarketCompatibility.Validate(
            CreateAirApp(apiVersion: "5.0.0", minHostVersion: "0.9.0"),
            new Version(0, 8, 8),
            "5.0.0");

        Assert.NotNull(error);
        Assert.Contains("requires host version 0.9.0 or newer", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsCompatibleAirApp()
    {
        var error = AirAppMarketCompatibility.Validate(
            CreateAirApp(apiVersion: "5.2.0", minHostVersion: "0.8.0"),
            new Version(0, 8, 8),
            "5.0.0");

        Assert.Null(error);
    }

    private static AirAppMarketAirAppEntry CreateAirApp(
        string apiVersion,
        string minHostVersion = "0.0.1") =>
        new()
        {
            AirAppId = "Example.AirApp",
            Id = "Example.AirApp",
            Name = "Example AirApp",
            Description = "Compatibility test plugin.",
            Author = "LanMountainDesktop",
            Version = "1.0.0",
            ApiVersion = apiVersion,
            MinHostVersion = minHostVersion,
            RepositoryUrl = "https://github.com/example/example-plugin",
            PackageSources =
            [
                new AirAppMarketAirAppPackageSourceEntry
                {
                    Kind = "workspaceLocal",
                    Url = "workspace://Example.AirApp/Example.AirApp.1.0.0.laapp",
                    SourceKind = AirAppPackageSourceKind.WorkspaceLocal
                }
            ]
        };
}
