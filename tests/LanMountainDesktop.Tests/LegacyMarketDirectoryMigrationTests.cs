using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// PluginMarket → AirAppMarket 的一次性目录迁移。
/// 关键是"离线兜底缓存改名后仍在原位"，否则迁移只是换个地方摆垃圾。
/// 与 AirAppRuntimeDataPathTests 共用 AppDataPathProvider 的静态状态，必须同组串行。
/// </summary>
[Collection("AppDataPath")]
public sealed class LegacyMarketDirectoryMigrationTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(LegacyMarketDirectoryMigrationTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void LegacyMarketDirectory_IsRenamedWithContentsIntact()
    {
        var legacyIndex = CreateLegacyCacheIndex("cached-index-payload");

        AppDataPathProvider.Initialize(["--data-root", _dataRoot]);

        Assert.False(Directory.Exists(Path.Combine(_dataRoot, "PluginMarket")));
        Assert.True(File.Exists(legacyIndex.Replace("PluginMarket", "AirAppMarket", StringComparison.Ordinal)),
            "市场索引缓存应能从新目录的同一相对路径读到");
    }

    [Fact]
    public void Migration_IsIdempotentAndKeepsMovedContent()
    {
        CreateLegacyCacheIndex("payload");
        AppDataPathProvider.Initialize(["--data-root", _dataRoot]);

        var movedIndex = Path.Combine(_dataRoot, "AirAppMarket", "cache", "index.json");
        Assert.Equal("payload", File.ReadAllText(movedIndex));

        // 再跑一次不应抛错、不应改动已就位的内容。
        AppDataPathProvider.MigrateLegacyMarketDirectory();
        Assert.Equal("payload", File.ReadAllText(movedIndex));
    }

    [Fact]
    public void ExistingMarketDirectory_WinsAndLegacyIsLeftAlone()
    {
        CreateLegacyCacheIndex("old");
        var currentIndex = Path.Combine(_dataRoot, "AirAppMarket", "cache", "index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(currentIndex)!);
        File.WriteAllText(currentIndex, "new");

        AppDataPathProvider.Initialize(["--data-root", _dataRoot]);

        Assert.True(Directory.Exists(Path.Combine(_dataRoot, "PluginMarket")),
            "两侧都存在时不得删除或覆盖任何一份用户数据");
        Assert.Equal("new", File.ReadAllText(currentIndex));
    }

    [Fact]
    public void NoLegacyDirectory_CreatesNothing()
    {
        Directory.CreateDirectory(_dataRoot);

        AppDataPathProvider.Initialize(["--data-root", _dataRoot]);

        Assert.False(Directory.Exists(Path.Combine(_dataRoot, "AirAppMarket")));
        Assert.False(Directory.Exists(Path.Combine(_dataRoot, "PluginMarket")));
    }

    private string CreateLegacyCacheIndex(string payload)
    {
        var indexPath = Path.Combine(_dataRoot, "PluginMarket", "cache", "index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.WriteAllText(indexPath, payload);
        Directory.CreateDirectory(Path.Combine(_dataRoot, "PluginMarket", "downloads"));

        return indexPath;
    }

    public void Dispose()
    {
        AppDataPathProvider.ResetForTests();
        try
        {
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
        catch
        {
        }
    }
}
