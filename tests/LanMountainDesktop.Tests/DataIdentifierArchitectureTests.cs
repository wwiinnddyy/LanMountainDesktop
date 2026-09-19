using System.Text.RegularExpressions;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 磁盘与包格式标识符（文件名、目录名、扩展名）在整个解决方案里必须只出现一次。
/// 之前 .pending-plugin-upgrades.json 与 plugin-settings.json 各被两个项目独立硬编码，
/// Launcher 的 .runtime 更是与宿主差一个点导致排除条件失效——这类字面量一旦分叉就是静默故障。
/// </summary>
public sealed class DataIdentifierArchitectureTests
{
    // manifest.json 不在清单内：AirAppMarketAssetCacheService / ZhiJiaoHubCacheService 里的
    // manifest.json 是各自缓存目录的清单文件名，与旧版包清单只是同名，不是同一概念。
    private static readonly string[] TrackedIdentifiers =
    [
        "airapp.json",
        ".laapp",
        ".lmdp",
        ".runtime",
        ".pending-deletions",
        ".pending-plugin-upgrades.json",
        "plugin-settings.json",
        "airapp-settings.json",
        ".pending-plugin-deletions.json",
        ".pending-airapp-deletions.json"
    ];

    private static readonly string[] ProductionDirectories =
    [
        "core",
        "desktop",
        "airapp",
        "install",
        "mobile"
    ];

    [Fact]
    public void OnDiskIdentifiers_AreDeclaredInOnlyOnePlace()
    {
        var productionFiles = ProductionDirectories
            .Select(part => Path.Combine(RepoRoot, part))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var offenders = TrackedIdentifiers
            .Select(identifier => (identifier, sites: FindLiteralSites(productionFiles, identifier)))
            .Where(pair => pair.sites.Length > 1)
            .Select(pair => $"\"{pair.identifier}\" appears in {pair.sites.Length} places: {string.Join(", ", pair.sites)}")
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string[] FindLiteralSites(IEnumerable<string> files, string identifier)
    {
        var pattern = new Regex($"\"{Regex.Escape(identifier)}\"");

        return files
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, index, line))
                // 注释里提到某个字面量是在解释为什么要保留它，不算第二处定义。
                .Where(t => !t.line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .Where(t => pattern.IsMatch(t.line))
                .Select(t => $"{RelativeToRepo(t.file)}:{t.index + 1}"))
            .ToArray();
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

    private static string RelativeToRepo(string path) => Path.GetRelativePath(RepoRoot, path);
}
