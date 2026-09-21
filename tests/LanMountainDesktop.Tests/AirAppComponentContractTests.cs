using Avalonia.Controls;

using LanMountainDesktop.AirApps;
using LanMountainDesktop.AirAppSdk;

using Xunit;

// 宿主与 Launcher 各自有一份 AirAppManifest（两份真源），这里必须点名 SDK 的那份。
using AirAppManifest = LanMountainDesktop.AirAppSdk.AirAppManifest;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 清单 <c>components[].id</c> 与代码注册的 ComponentId 必须逐字一致。
/// 这条规则是被真实事故逼出来的：LanWord 的清单写 <c>lanword.lookup.widget</c>、
/// 代码注册 <c>lanword.lookup.word-card</c>，宿主一声不响，用户看到的是添加面板里一个点不动的格子
/// （面板按清单列，创建控件按注册 id 找）。6 个外部 AirApp 里就撞上这么一起。
/// </summary>
public sealed class AirAppComponentContractTests
{
    private const string DeclaredId = "com.test.widget";

    [Fact]
    public void DeclaredComponentWithoutRegistration_IsRejected_NamingBothSides()
    {
        // 作者拿到这条消息就该能自己修：缺谁、多谁、以及"清单是唯一真源"这条口径。
        var error = Assert.Throws<InvalidOperationException>(() =>
            AirAppLoader.ValidateManifestComponentContract(
                Manifest(DeclaredId),
                [Registration("com.test.word-card")]));

        Assert.Contains(DeclaredId, error.Message, StringComparison.Ordinal);
        Assert.Contains("com.test.word-card", error.Message, StringComparison.Ordinal);
        Assert.Contains("清单是唯一真源", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisteredComponentMissingFromManifest_OnlyWarns()
    {
        // 注册了却没声明：用户在面板里拿不到它，但不产生"看得见点不动"的坏状态；
        // 拒载会把同一个包里能用的窗口与 IPC 服务一起牺牲掉，所以只警告。
        AirAppLoader.ValidateManifestComponentContract(
            Manifest(DeclaredId),
            [Registration(DeclaredId), Registration("com.test.undeclared")]);
    }

    [Fact]
    public void MatchingIds_AreAccepted()
    {
        AirAppLoader.ValidateManifestComponentContract(
            Manifest(DeclaredId),
            [Registration(DeclaredId)]);
    }

    [Fact]
    public void ManifestWithoutComponents_IsNotAnError()
    {
        // 只提供窗口或 IPC 的 AirApp 是合法形态。
        AirAppLoader.ValidateManifestComponentContract(
            Manifest(),
            [Registration(DeclaredId)]);
    }

    private static AirAppManifest Manifest(params string[] componentIds) =>
        new("com.test.app", "测试包", "com.test.app.dll",
            Components: componentIds
                .Select(id => new AirAppComponentManifest(id, id))
                .ToArray());

    private static AirAppComponentRegistration Registration(string componentId) =>
        new(_ => new Control(), new AirAppComponentOptions
        {
            ComponentId = componentId,
            DisplayName = "测试组件",
            IconKey = "Box",
            Category = "测试",
        });
}
