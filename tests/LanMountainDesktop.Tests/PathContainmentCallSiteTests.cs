using System;
using System.IO;

using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Shared.Contracts.Launcher;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 三处"包根/树根包含判定"曾经用裸前缀比较，本文件钉住它们改走 <c>PathContainment</c> 之后的行为。
///
/// 为什么这三处值得单独钉（而不是只钉那个家）：家本身是对的（<c>PathContainmentTests</c> 早就钉过兄弟前缀），
/// 但**调用点把两个参数写反**或"顺手又写回 <c>StartsWith</c>"都不会编译报错，只会让那道闸静默敞开。
/// 每格都对着一个具体的错法：
/// <c>UpdatePathGuard.EnsurePathWithinRoot</c> 的异常文案写的是 "Path traversal detected"，
/// 可原来那行 <c>target.StartsWith(root)</c> 对 <c>C:\pkg-evil\1.zip</c> 是放行的——文案与实行为相反；
/// <c>PlondsPayloadResolver</c> 靠这道过滤决定"这个对象文件能不能读"，放行兄弟目录等于读包外的字节；
/// <c>AppVersionProvider</c> 拿它判"这个 exe 目录属不属于这个包根"，算错的后果是启动器挑中一个
/// 不在包根下的版本目录（症状是更新后起在缺文件的目录上）。
/// </summary>
public sealed class PathContainmentCallSiteTests
{
    [Fact]
    public void UpdatePathGuard_RejectsSiblingThatSharesTheRootPrefix()
    {
        var (root, sibling) = NewRootAndSibling();
        try
        {
            var inside = Path.Combine(root, "sub", "ok.txt");

            UpdatePathGuard.EnsurePathWithinRoot(inside, root);
            UpdatePathGuard.EnsurePathWithinRoot(root, root);

            Assert.Throws<InvalidOperationException>(
                () => UpdatePathGuard.EnsurePathWithinRoot(
                    Path.Combine(sibling, "payload.zip"), root));
        }
        finally
        {
            Delete(root, sibling);
        }
    }

    [Fact]
    public void PayloadResolver_ReadsNothingFromASiblingIncomingTree()
    {
        var launcherRoot = Path.Combine(Path.GetTempPath(), "LMD.PayloadResolve", Guid.NewGuid().ToString("N"));
        var paths = new PlondsApplyPaths(launcherRoot);
        Directory.CreateDirectory(paths.IncomingRoot);
        var sibling = paths.IncomingRoot + "_evil";
        Directory.CreateDirectory(sibling);
        // 两个文件**必须不同名**：解析器除了原样键还会补 `objects/<文件名>` 这一格候选，
        // 第一版两处用了同一个文件名，于是"包外那份"被包内同名那份顶掉，负例根本测不到那条路。
        var outsidePayload = Path.Combine(sibling, "outside-only.bin");
        File.WriteAllText(outsidePayload, "not-yours");
        var insidePayload = Path.Combine(paths.IncomingRoot, "objects", "inside-only.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(insidePayload)!);
        File.WriteAllText(insidePayload, "mine");

        try
        {
            var resolver = new PlondsPayloadResolver(paths);

            // 兄弟目录里那个文件必须在清单上被拒掉——这一格就是原来那行裸前缀比较放行的那条路。
            Assert.Throws<FileNotFoundException>(() => resolver.ResolveObjectPath(new ApplyPlondsFileEntry
            {
                Path = "payload.bin",
                ObjectPath = outsidePayload,
            }));

            // 正向对照：包内的相对键照常解析得到，别把闸关死当成"修好了"。
            var resolved = resolver.ResolveObjectPath(new ApplyPlondsFileEntry
            {
                Path = "payload.bin",
                ObjectPath = Path.Combine("objects", "inside-only.bin"),
            });
            Assert.Equal(Path.GetFullPath(insidePayload), resolved);
        }
        finally
        {
            Directory.Delete(launcherRoot, recursive: true);
            if (Directory.Exists(sibling))
            {
                Directory.Delete(sibling, recursive: true);
            }
        }
    }

    [Fact]
    public void VersionProvider_DoesNotTreatSiblingPrefixedDirectoryAsThePackageRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LMD.VersionRoot", Guid.NewGuid().ToString("N"));
        var sibling = root + "_evil";
        var realDeployment = Path.Combine(root, "app-1.0.0");
        var evilDeployment = Path.Combine(sibling, "app-9.9.9");
        Directory.CreateDirectory(realDeployment);
        Directory.CreateDirectory(evilDeployment);
        File.WriteAllText(Path.Combine(realDeployment, "LanMountainDesktop.exe"), string.Empty);
        File.WriteAllText(Path.Combine(realDeployment, ".current"), string.Empty);
        File.WriteAllText(Path.Combine(realDeployment, "version.json"), """
        {"Version":"1.0.0","Codename":"Stonecutter"}
        """);
        File.WriteAllText(Path.Combine(evilDeployment, "LanMountainDesktop.exe"), string.Empty);
        File.WriteAllText(Path.Combine(evilDeployment, "version.json"), """
        {"Version":"9.9.9","Codename":"Ghost"}
        """);

        try
        {
            // 兄弟目录里那个 exe 不能把版本解析带跑：它不在包根里。
            var fromSiblingExe = AppVersionProvider.Resolve(
                packageRoot: root,
                deploymentDirectory: null,
                executablePath: Path.Combine(evilDeployment, "LanMountainDesktop.exe"));
            Assert.Equal("1.0.0", fromSiblingExe.Version);

            // 正向对照：包根内的子目录仍算"在里面"，别把闸关死。
            var fromInsideExe = AppVersionProvider.Resolve(
                packageRoot: root,
                deploymentDirectory: null,
                executablePath: Path.Combine(realDeployment, "LanMountainDesktop.exe"));
            Assert.Equal("1.0.0", fromInsideExe.Version);
        }
        finally
        {
            Delete(root, sibling);
        }
    }

    private static (string Root, string Sibling) NewRootAndSibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "LMD.ContainCall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sibling = root + "_evil";
        Directory.CreateDirectory(sibling);
        return (root, sibling);
    }

    private static void Delete(params string[] dirs)
    {
        foreach (var dir in dirs)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
