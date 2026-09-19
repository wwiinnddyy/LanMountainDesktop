using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 磁盘名改名归位的通用语义：只改名、不合并、不删除、幂等。
/// plugin-settings.json → airapp-settings.json 与
/// .pending-plugin-deletions.json → .pending-airapp-deletions.json 都走这里。
/// </summary>
public sealed class LegacyPathMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(LegacyPathMigrationTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void LegacyFile_IsRenamedWithContentsIntact()
    {
        var legacy = WriteFile("plugin-settings.json", "{\"a\":1}");
        var current = Path.Combine(_root, "airapp-settings.json");

        Assert.True(AppDataPathProvider.TryMoveLegacyPath(legacy, current, "test file"));
        Assert.False(File.Exists(legacy));
        Assert.Equal("{\"a\":1}", File.ReadAllText(current));
    }

    [Fact]
    public void SecondCall_IsNoOp()
    {
        var legacy = WriteFile("plugin-settings.json", "payload");
        var current = Path.Combine(_root, "airapp-settings.json");

        Assert.True(AppDataPathProvider.TryMoveLegacyPath(legacy, current, "test file"));
        Assert.False(AppDataPathProvider.TryMoveLegacyPath(legacy, current, "test file"));
        Assert.Equal("payload", File.ReadAllText(current));
    }

    [Fact]
    public void WhenCurrentAlreadyExists_NeitherSideIsTouched()
    {
        WriteFile("plugin-settings.json", "old");
        var current = Path.Combine(_root, "airapp-settings.json");
        File.WriteAllText(current, "new");

        var moved = AppDataPathProvider.TryMoveLegacyPath(
            Path.Combine(_root, "plugin-settings.json"),
            current,
            "test file");

        Assert.False(moved);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_root, "plugin-settings.json")));
        Assert.Equal("new", File.ReadAllText(current));
    }

    [Fact]
    public void NothingToMigrate_ReturnsFalseAndCreatesNothing()
    {
        Assert.False(AppDataPathProvider.TryMoveLegacyPath(
            Path.Combine(_root, "missing.json"),
            Path.Combine(_root, "created.json"),
            "test file"));

        Assert.False(File.Exists(Path.Combine(_root, "created.json")));
    }

    [Fact]
    public void LegacyDirectory_IsAlsoMovedIntact()
    {
        var legacyDir = Path.Combine(_root, "PluginMarket", "cache");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "index.json"), "cached");

        var target = Path.Combine(_root, "AirAppMarket");

        Assert.True(AppDataPathProvider.TryMoveLegacyPath(
            Path.Combine(_root, "PluginMarket"),
            target,
            "market directory"));
        Assert.Equal("cached", File.ReadAllText(Path.Combine(target, "cache", "index.json")));
    }

    private string WriteFile(string fileName, string content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, content);

        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }
}
