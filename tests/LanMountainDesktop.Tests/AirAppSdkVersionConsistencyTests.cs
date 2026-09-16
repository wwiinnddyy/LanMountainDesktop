using System.Text.RegularExpressions;
using LanMountainDesktop.AirAppSdk;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 守卫 AirApp 发布物的单一版本线：AirAppSdkInfo、AirAppSdk 包、Core 包与
/// dotnet new 模板默认版本必须保持一致。
/// （历史上 PluginSdk 曾出现 ApiVersion=5.0.0 而包版本=6.0.0、Core=6.0.0 而 SDK=1.0.0 的割裂。）
/// </summary>
public sealed class AirAppSdkVersionConsistencyTests
{
    [Fact]
    public void ApiVersion_Major_MatchesAssemblyVersion()
    {
        var apiVersion = System.Version.Parse(AirAppSdkInfo.ApiVersion);
        var assemblyVersion = typeof(AirAppSdkInfo).Assembly.GetName().Version;

        Assert.NotNull(assemblyVersion);
        Assert.Equal(apiVersion.Major, assemblyVersion!.Major);
    }

    [Fact]
    public void SdkVersion_MatchesApiVersion()
    {
        Assert.Equal(AirAppSdkInfo.SdkVersion, AirAppSdkInfo.ApiVersion);
    }

    [Fact]
    public void ManifestFileName_IsAirappJson()
    {
        Assert.Equal("airapp.json", AirAppSdkInfo.ManifestFileName);
    }

    [Fact]
    public void SdkPackageVersion_MatchesSdkVersion()
    {
        Assert.Equal(AirAppSdkInfo.SdkVersion, ReadProjectVersion(
            Path.Combine(RepoRoot, "airapp", "LanMountainDesktop.AirAppSdk", "LanMountainDesktop.AirAppSdk.csproj")));
    }

    [Fact]
    public void CorePackageVersion_MatchesSdkVersion()
    {
        // Core 与 SDK 一起发布，且 SDK 包硬依赖 Core 包；版本线必须一致。
        Assert.Equal(AirAppSdkInfo.SdkVersion, ReadProjectVersion(
            Path.Combine(RepoRoot, "core", "LanMountainDesktop.Core", "LanMountainDesktop.Core.csproj")));
    }

    [Fact]
    public void TemplatePackageVersion_MatchesSdkVersion()
    {
        Assert.Equal(AirAppSdkInfo.SdkVersion, ReadProjectVersion(
            Path.Combine(RepoRoot, "airapp", "LanMountainDesktop.AirAppTemplate", "LanMountainDesktop.AirAppTemplate.csproj")));
    }

    [Fact]
    public void TemplateDefaultSdkVersion_MatchesSdkVersion()
    {
        // 模板生成的项目默认引用的 SDK 包版本，必须是本仓库正在发布的版本，
        // 否则 dotnet new 出来的工程一还原就失败。
        var templateConfig = File.ReadAllText(Path.Combine(
            RepoRoot,
            "airapp",
            "LanMountainDesktop.AirAppTemplate",
            "content",
            ".template.config",
            "template.json"));

        using var document = System.Text.Json.JsonDocument.Parse(templateConfig);
        var defaultValue = document.RootElement
            .GetProperty("symbols")
            .GetProperty("airAppSdkVersion")
            .GetProperty("defaultValue")
            .GetString();

        Assert.Equal(AirAppSdkInfo.SdkVersion, defaultValue);
    }

    [Fact]
    public void TemplateManifest_DeclaresCurrentApiVersion()
    {
        var manifest = File.ReadAllText(Path.Combine(
            RepoRoot,
            "airapp",
            "LanMountainDesktop.AirAppTemplate",
            "content",
            AirAppSdkInfo.ManifestFileName));

        using var document = System.Text.Json.JsonDocument.Parse(manifest);
        var apiVersion = document.RootElement.GetProperty("apiVersion").GetString();

        Assert.Equal(AirAppSdkInfo.ApiVersion, apiVersion);
    }

    private static string ReadProjectVersion(string projectPath)
    {
        var match = Regex.Match(
            File.ReadAllText(projectPath),
            @"<Version>(?<version>[^<]+)</Version>",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        Assert.True(match.Success, $"'{projectPath}' does not declare a <Version> element.");
        return match.Groups["version"].Value.Trim();
    }

    private static string RepoRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "LanMountainDesktop.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Unable to locate repository root.");
        }
    }
}
