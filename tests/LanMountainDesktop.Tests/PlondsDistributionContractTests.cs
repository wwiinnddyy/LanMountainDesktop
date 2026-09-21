using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using LanMountainDesktop.Services.Update;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 宿主与 PLONDS 分发元数据之间的清单契约。仓库里带着服务端自己生成的样例元数据
/// （PenguinLogisticsOnlineNetworkDistributionSystem/sample-data），它的条目键名是
/// path / op / contentHash / size / mode / objectKey，而增量 filemap 用的是 action / sha256。
/// 宿主解析器曾只认后者：于是读真实分发元数据时每条都静默降级成 action=replace 且没有校验值。
/// 这条测试拿仓库内的真实样本钉住两种键名，任何一边改名都会红。
/// </summary>
public sealed class PlondsDistributionContractTests
{
    private const string DistributionDirectory = "PenguinLogisticsOnlineNetworkDistributionSystem/sample-data/meta/distributions";

    public static TheoryData<string> SampleDistributions()
    {
        var data = new TheoryData<string>();
        var directory = Path.Combine(RepoRoot, DistributionDirectory);
        if (!Directory.Exists(directory))
        {
            return data;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(file);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SampleDistributions))]
    public void HostParser_PreservesOpAndContentHashFromRealSamples(string path)
    {
        var json = File.ReadAllText(path);
        var expected = ReadSampleEntries(json);
        Assert.NotEmpty(expected);

        var entries = ParseHostSide(json);

        foreach (var (entryPath, op, contentHash) in expected)
        {
            var parsed = Assert.Single(entries, item => string.Equals(item.Path, entryPath, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(op, parsed.Action);
            Assert.Equal(contentHash, parsed.Sha256);
        }
    }

    [Fact]
    public void HostParser_StillReadsDeltaFileMapKeyNames()
    {
        const string fileMap = """
            {
              "version": "1.2.3",
              "files": [
                { "path": "app/LanMountainDesktop.dll", "action": "reuse", "sha256": "aa11" },
                { "path": "app/New.dll", "action": "add", "filesha256": "bb22" }
              ]
            }
            """;

        var entries = ParseHostSide(fileMap);

        Assert.Collection(
            entries.OrderBy(item => item.Path, StringComparer.Ordinal),
            first =>
            {
                Assert.Equal("app/LanMountainDesktop.dll", first.Path);
                Assert.Equal("reuse", first.Action);
                Assert.Equal("aa11", first.Sha256);
            },
            second =>
            {
                Assert.Equal("app/New.dll", second.Path);
                Assert.Equal("add", second.Action);
                Assert.Equal("bb22", second.Sha256);
            });
    }

    private static List<ApplyPlondsFileEntry> ParseHostSide(string json)
    {
        var fileMap = new ApplyPlondsFileMap();
        var entries = new List<ApplyPlondsFileEntry>();
        PlondsManifestParser.PopulateFromRawJson(json, fileMap, entries);
        return entries;
    }

    private static List<(string Path, string Op, string ContentHash)> ReadSampleEntries(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rows = new List<(string, string, string)>();

        if (!document.RootElement.TryGetProperty("components", out var components))
        {
            return rows;
        }

        foreach (var component in components.EnumerateArray())
        {
            if (!component.TryGetProperty("files", out var files))
            {
                continue;
            }

            foreach (var file in files.EnumerateArray())
            {
                rows.Add((
                    file.GetProperty("path").GetString()!,
                    file.GetProperty("op").GetString()!,
                    file.GetProperty("contentHash").GetString()!));
            }
        }

        return rows;
    }

    private static string RepoRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, DistributionDirectory)))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("未找到 PLONDS 样例元数据目录。");
        }
    }
}
