using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.Services.AirAppMarket;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 卸载轻应用时要跟着摘掉的那份市场资产缓存。这条能力在宿主里躺了很久没人调，2026-09-27 才接上
/// <c>AirAppRuntimeService.DeleteInstalledAirAppCore</c>（G1-BB：登记写着"宿主没有卸载路径"，现量不成立——
/// 设置页的"删除"一直是活的，用的动词是 <c>Delete</c> 不是 <c>Uninstall</c>，登记按 Uninstall grep 才漏了）。
/// 接线之外这里钉的是 <c>Invalidate</c> 自己的三件事，少任何一件都不报错：
/// 只改内存的话新实例会指向一个已经不存在的文件；整个清掉的话别人的缓存跟着陪葬；
/// 没缓存过的 id 若"条目不存在就抛"，删除一个从没在市场点开过详情的应用会在收尾那步炸掉。
/// </summary>
public sealed class AirAppMarketAssetCacheTests : IDisposable
{
    private const string ReadmeUrl = "https://example.test/demo/readme.md";
    private const string IconUrl = "https://example.test/demo/icon.png";
    private const string Version = "1.0.0";

    private readonly string _marketDirectory =
        Path.Combine(Path.GetTempPath(), "market-asset-cache-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Invalidate_DropsTheEntryTheFileAndThePersistedManifest()
    {
        await StoreReadmeAsync("demo.airapp");

        using (var cache = new AirAppMarketAssetCacheService(_marketDirectory))
        {
            Assert.NotNull(cache.TryGetReadme("demo.airapp", ReadmeUrl, Version));
            // 先确认"我以为的文件路径"就是家真正写出去的那个文件——否则下面那条"文件不见了"
            // 会因为路径算错而假绿。
            Assert.True(File.Exists(ReadmePath("demo.airapp")), "测试算的 README 路径与家写的不一致，先修这条。");
            cache.Invalidate("demo.airapp");
            Assert.Null(cache.TryGetReadme("demo.airapp", ReadmeUrl, Version));
        }

        // 家把 appId 过一个"非法字符换下划线"的名字清洗，这里的 id 刻意不含任何非法字符，
        // 所以期望文件名就是 id 本身——不在测试里再抄一份清洗逻辑（那会变成第四份实现）。
        Assert.False(File.Exists(ReadmePath("demo.airapp")), "缓存条目清了，README 文件却还留在盘上。");

        // 换一个实例重读盘：钉的是这次清理写进了 manifest.json，而不只是改了内存。
        // TryGet* 只认清单里的条目、不检查文件在不在，所以漏写盘的症状是"回一个不存在的路径"。
        using (var reloaded = new AirAppMarketAssetCacheService(_marketDirectory))
        {
            Assert.Null(reloaded.TryGetReadme("demo.airapp", ReadmeUrl, Version));
        }
    }

    /// <summary>
    /// 只摘被卸载那一个应用的资产。写成 <c>ClearAll()</c> 也能让上一条测试绿，
    /// 代价是用户删一个轻应用就把整个市场缓存清空——下次翻市场要重下所有 README 与图标。
    /// </summary>
    [Fact]
    public async Task Invalidate_KeepsEveryOtherAirAppCached()
    {
        await StoreReadmeAsync("demo.airapp");
        await StoreReadmeAsync("other.airapp");

        using (var cache = new AirAppMarketAssetCacheService(_marketDirectory))
        {
            cache.Invalidate("demo.airapp");
        }

        using (var reloaded = new AirAppMarketAssetCacheService(_marketDirectory))
        {
            Assert.Null(reloaded.TryGetReadme("demo.airapp", ReadmeUrl, Version));
            Assert.NotNull(reloaded.TryGetReadme("other.airapp", ReadmeUrl, Version));
            Assert.True(File.Exists(ReadmePath("other.airapp")));
        }
    }

    /// <summary>
    /// 没缓存过的 id 要安静返回。卸载路径上这是常态。
    /// </summary>
    [Fact]
    public void Invalidate_UnknownId_IsQuietNoOp()
    {
        using var cache = new AirAppMarketAssetCacheService(_marketDirectory);
        Assert.Null(Record.Exception(() => cache.Invalidate("never-cached.airapp")));
    }

    /// <summary>
    /// <b>钉的是现状，不是理想行为</b>：manifest 按 appId 存<b>一条</b>记录、记录里带资产种类，
    /// 所以同一个应用的 README 与图标互相覆盖——存完图标再问 README，答案是"没缓存"，
    /// 于是每次看详情都重下一遍 README，而那个 .md 文件已经静静躺在盘上。
    /// 这条形状已登记（G1-CJ）：要修得先改 manifest 的结构（一个 id 两条记录），属磁盘格式变更。
    /// 把这条测试改绿之前请先看那条登记，别在这里顺手改成"两个都在"。
    /// </summary>
    [Fact]
    public async Task OneAirApp_CanOnlyTrackOneAssetKind_Today()
    {
        await StoreReadmeAsync("demo.airapp");

        using (var cache = new AirAppMarketAssetCacheService(_marketDirectory))
        {
            Assert.NotNull(cache.TryGetReadme("demo.airapp", ReadmeUrl, Version));
        }

        using (var cache = new AirAppMarketAssetCacheService(_marketDirectory))
        {
            var icon = new MemoryStream([1, 2, 3, 4]);
            await cache.StoreIconAsync("demo.airapp", IconUrl, Version, icon, CancellationToken.None);
            Assert.Null(cache.TryGetReadme("demo.airapp", ReadmeUrl, Version));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_marketDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录删不掉不影响结论。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task StoreReadmeAsync(string airAppId)
    {
        using var cache = new AirAppMarketAssetCacheService(_marketDirectory);
        using var readme = new MemoryStream(Encoding.UTF8.GetBytes("# readme"));
        await cache.StoreReadmeAsync(airAppId, ReadmeUrl, Version, readme, CancellationToken.None);
    }

    private string ReadmePath(string airAppId) =>
        Path.Combine(_marketDirectory, "cache", "assets", "readme", airAppId + ".md");
}
