using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using LanMountainDesktop.AirApps;
using LanMountainDesktop.AirAppSdk;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 共享契约的回收（#G1-CO）。
///
/// 这块盘以前<b>一条删除路径都没有</b>：契约按 <c>{data}/SharedContracts/{id}/{version}/{assembly}</c> 落盘，
/// 市场每滚一版就多留一份旧程序集，谁也不回收——与 G1-BB 刚补的市场资产缓存是同一类"只涨不减"。
/// 但也不能"卸载就删"：契约是<b>跨应用共享</b>的（同一 id+version 只落一份，多个包都指它），
/// 所以保留集合必须是"盘上现存包（含被禁用的）的清单引用"，
/// 而清单为空时干脆什么都不删——空清单更像"发现环节坏了"，而不是"用户把所有包都卸了"，
/// 拿这个猜下去会把所有契约删光，离线用户就此修不回来。
/// </summary>
public sealed class SharedContractPruningTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "LMD.SharedContractPruning",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void PruneUnused_KeepsWhatAPresentPackageStillReferences()
    {
        using var manager = new AirAppSharedContractManager(_dataRoot);
        var kept = WriteContract(manager, "contract.shared", "1.2.0", "Shared.dll");
        var orphan = WriteContract(manager, "contract.shared", "1.1.0", "Shared.dll");
        var orphanFromKilledDownload = WriteContract(manager, "contract.gone", "9.9.9", "Gone.dll.download");

        var deleted = manager.PruneUnused([ManifestWithContract("contract.shared", "1.2.0", "Shared.dll")]);

        Assert.Equal(2, deleted);
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(orphan));
        Assert.False(File.Exists(orphanFromKilledDownload));
    }

    [Fact]
    public void PruneUnused_SweepsADirectoryOnceItHoldsNothing()
    {
        using var manager = new AirAppSharedContractManager(_dataRoot);
        WriteContract(manager, "contract.gone", "1.0.0", "Gone.dll");

        Assert.Equal(1, manager.PruneUnused([ManifestWithContract("contract.kept", "1.0.0", "Kept.dll")]));
        Assert.False(Directory.Exists(Path.Combine(manager.ContractsDirectory, "contract.gone")));
    }

    [Fact]
    public void PruneUnused_WithNoPackagesOnDisk_DeletesNothingAtAll()
    {
        using var manager = new AirAppSharedContractManager(_dataRoot);
        var existing = WriteContract(manager, "contract.shared", "1.0.0", "Shared.dll");

        // 空清单按"没量到"处理：宁可什么都不删。
        Assert.Equal(0, manager.PruneUnused(Array.Empty<AirAppManifest>()));
        Assert.True(File.Exists(existing));
    }

    [Fact]
    public void PruneUnused_KeepsEveryVersionASecondPackageStillNeeds()
    {
        using var manager = new AirAppSharedContractManager(_dataRoot);
        var sharedByBoth = WriteContract(manager, "contract.shared", "1.0.0", "Shared.dll");
        var onlySecondNeeds = WriteContract(manager, "contract.other", "2.0.0", "Other.dll");

        var deleted = manager.PruneUnused([
            ManifestWithContract("contract.shared", "1.0.0", "Shared.dll"),
            ManifestWithContract("contract.other", "2.0.0", "Other.dll"),
        ]);

        Assert.Equal(0, deleted);
        Assert.True(File.Exists(sharedByBoth));
        Assert.True(File.Exists(onlySecondNeeds));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static AirAppManifest ManifestWithContract(string id, string version, string assemblyName) =>
        new("app.pruning", "Pruning", "entry.dll", SharedContracts: [new AirAppSharedContractReference(id, version, assemblyName)]);

    private static string WriteContract(AirAppSharedContractManager manager, string id, string version, string assemblyName)
    {
        var path = Path.Combine(manager.ContractsDirectory, id, version, assemblyName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "bytes");
        return path;
    }
}
