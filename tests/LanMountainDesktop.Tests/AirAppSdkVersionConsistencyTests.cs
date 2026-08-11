using LanMountainDesktop.AirAppSdk;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 断言 AirAppSdkInfo 的 API 版本与程序集/包版本主版本一致，防止再次背离
/// （历史上 PluginSdk 曾出现 ApiVersion=5.0.0 而包版本=6.0.0 的割裂）。
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
}
