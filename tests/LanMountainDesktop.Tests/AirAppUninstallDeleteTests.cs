using System;
using System.Collections.Generic;
using System.IO;

using LanMountainDesktop.Services;
using LanMountainDesktop.Shared.IO;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 卸载时删包这一步的行为钉（#G1-CK 的第一条）。
///
/// 动因：这里原本是一份手写的"尽力删除"抄本——空的 <c>catch</c> 直接 <c>return false</c>。
/// 它有两个可测的后果：<b>只读文件删不掉</b>（<c>File.Delete</c> 对只读属性抛异常，
/// 而那家会先把属性清掉），以及<b>删不动的原因不出声</b>——
/// 症状就是"卸载跑完了，AppData 里还留着那个应用的目录"，而日志里一行都查不到。
/// 家（<c>FileOperationRetryHelper</c>）早就把这两件事收口了，这里只是不再绕开它。
/// </summary>
public sealed class AirAppUninstallDeleteTests : IDisposable
{
    private readonly string _root;

    public AirAppUninstallDeleteTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LMD.AirAppUninstallDelete", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            File.SetAttributes(_root, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ReadOnlyPackageFile_IsDeletedRatherThanLeftBehind()
    {
        var package = Path.Combine(_root, "readonly-app.airapp");
        File.WriteAllText(package, "package-bytes");
        File.SetAttributes(package, FileAttributes.ReadOnly);

        Assert.True(
            AirAppRuntimeService.TryDeleteAirAppTarget(package),
            "只读的包文件删不动——那正是原来那份抄本的形状（File.Delete 对只读属性抛异常，空 catch 吞掉）");
        Assert.False(File.Exists(package));
    }

    [Fact]
    public void MissingPackagePath_CountsAsDeletedSoNothingGetsQueuedForRestart()
    {
        var absent = Path.Combine(_root, "never-existed.airapp");

        // 返回 false 会让调用方把这个根本不存在的路径记进 .pending，下次启动再白删一趟。
        Assert.True(AirAppRuntimeService.TryDeleteAirAppTarget(absent));
    }

    [Fact]
    public void LockedPackageDirectory_SaysWhyItFailed_InsteadOfVanishingSilently()
    {
        var directory = Path.Combine(_root, "locked-app");
        Directory.CreateDirectory(directory);
        var held = Path.Combine(directory, "app.dll");
        File.WriteAllText(held, "in-use");

        var notices = new List<string>();
        var previous = FileOperationRetryHelper.FailureNotice;
        FileOperationRetryHelper.FailureNotice =
            (category, message, _) => notices.Add($"{category}|{message}");

        try
        {
            using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.False(AirAppRuntimeService.TryDeleteAirAppTarget(directory));
            }

            Assert.Contains(notices, n => n.StartsWith("AirAppRuntime|", StringComparison.Ordinal));
        }
        finally
        {
            FileOperationRetryHelper.FailureNotice = previous;
        }
    }
}
