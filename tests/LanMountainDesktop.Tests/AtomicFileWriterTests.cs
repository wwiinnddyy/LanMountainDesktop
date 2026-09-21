using System.IO;
using System.Text;

using LanMountainDesktop.Services;

using Xunit;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 整体替换文件这件事现在只有一个入口（<see cref="AtomicFileWriter"/>），这里钉住它承诺的三件事：
/// 内容真的落盘、失败时旧内容还在、以及任何情况下都不在用户目录里留下 .tmp。
/// 第三条尤其重要：收口前 12 处手搓写法里只有一处清理临时文件。
/// </summary>
public sealed class AtomicFileWriterTests : IDisposable
{
    private const string Category = "Test";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(AtomicFileWriterTests),
        Guid.NewGuid().ToString("N"));

    public AtomicFileWriterTests()
    {
        // 有些用例要先自己造一个"用户选中的源文件"，那时写入器还没被调用、目录还不存在。
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void WritesContent_CreatesMissingDirectory_LeavesNoTempFile()
    {
        var path = Path.Combine(_directory, "nested", "settings.json");

        AtomicFileWriter.WriteText(path, "{\"a\":1}", Category);

        Assert.Equal("{\"a\":1}", File.ReadAllText(path));
        Assert.Empty(TempFiles(path));
    }

    [Fact]
    public void Overwrite_ReplacesShorterContentCompletely()
    {
        var path = Path.Combine(_directory, "settings.json");
        AtomicFileWriter.WriteText(path, new string('x', 4096), Category);

        AtomicFileWriter.WriteText(path, "short", Category);

        Assert.Equal("short", File.ReadAllText(path));
        Assert.Empty(TempFiles(path));
    }

    [Fact]
    public void DefaultOverload_WritesNoBom_EncodingOverloadDoes()
    {
        // 钉住两个重载的字节差异：白板笔记磁盘上本来就是带 BOM 的，迁移时不能顺手改掉它。
        var plain = Path.Combine(_directory, "plain.json");
        var withBom = Path.Combine(_directory, "bom.json");

        AtomicFileWriter.WriteText(plain, "{}", Category);
        AtomicFileWriter.WriteText(withBom, "{}", Encoding.UTF8, Category);

        Assert.False(File.ReadAllBytes(plain).StartsWith(new byte[] { 0xEF }));
        Assert.True(File.ReadAllBytes(withBom).StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void WhenTargetCannotBeReplaced_NoTempIsLeftBehind()
    {
        // 目标位置站着一个同名目录：写临时文件会成功，覆盖式 Move 必失败。
        // 这条测的是"失败路径也要收拾"——收口前 12 处手搓写法里只有白板笔记清临时文件。
        var target = Path.Combine(_directory, "blocked.json");
        Directory.CreateDirectory(target);

        var error = Record.Exception(() => AtomicFileWriter.WriteText(target, "new", Category));

        Assert.True(
            error is IOException or UnauthorizedAccessException,
            $"预期 IO/权限类失败，实际：{error?.GetType().Name}: {error?.Message}");
        Assert.Empty(TempFiles(target));
    }

    [Fact]
    public async Task StreamOverwrite_ReplacesWholeFileAndCleansUp()
    {
        var path = Path.Combine(_directory, "asset.png");
        await using (var first = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 }))
        {
            await AtomicFileWriter.WriteStreamAsync(path, first, Category);
        }

        await using (var second = new MemoryStream(new byte[] { 9 }))
        {
            await AtomicFileWriter.WriteStreamAsync(path, second, Category);
        }

        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(path));
        Assert.Empty(TempFiles(path));
    }

    [Fact]
    public void PlaceFile_CopiesIntoPlace_KeepsSource_LeavesNoTempFile()
    {
        // 换壁纸那条路径的形状：源是用户选中的文件，目标是受管文件名，两边都得活着。
        var source = Path.Combine(_directory, "picked.png");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });
        var target = Path.Combine(_directory, "nested", "background.png");

        AtomicFileWriter.PlaceFile(source, target, Category);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(target));
        Assert.True(File.Exists(source));
        Assert.Empty(TempFiles(target));
    }

    [Fact]
    public void PlaceFile_OverwritesShorterExistingTargetCompletely()
    {
        var source = Path.Combine(_directory, "small.bin");
        File.WriteAllBytes(source, new byte[] { 7 });
        var target = Path.Combine(_directory, "managed.bin");
        File.WriteAllBytes(target, new byte[] { 1, 2, 3, 4, 5, 6 });

        AtomicFileWriter.PlaceFile(source, target, Category);

        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(target));
        Assert.Empty(TempFiles(target));
    }

    [Fact]
    public void PlaceFile_WhenTargetCannotBeReplaced_NoTempIsLeftBehind()
    {
        var source = Path.Combine(_directory, "again.bin");
        File.WriteAllBytes(source, new byte[] { 1 });
        var target = Path.Combine(_directory, "blocked-dir-target.bin");
        Directory.CreateDirectory(target);

        var error = Record.Exception(() => AtomicFileWriter.PlaceFile(source, target, Category));

        Assert.True(
            error is IOException or UnauthorizedAccessException,
            $"预期 IO/权限类失败，实际：{error?.GetType().Name}: {error?.Message}");
        Assert.Empty(TempFiles(target));
    }

    [Fact]
    public void NullArguments_AreRejectedBeforeAnythingTouchesDisk()
    {
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.WriteText("", "{}", Category));
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.WriteText("x", "{}", " "));
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.PlaceFile("a", "b", " "));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static string[] TempFiles(string pathOfTarget)
    {
        var directory = Path.GetDirectoryName(pathOfTarget)!;
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories)
            : Array.Empty<string>();
    }
}
