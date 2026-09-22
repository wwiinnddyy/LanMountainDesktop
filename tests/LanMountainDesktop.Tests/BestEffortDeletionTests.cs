using LanMountainDesktop.Shared.IO;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 尽力删除的口径：清得掉就清，清不掉回报一声而不是静默。
/// 收口前 13 份逐字/近似复制散在三个二进制里，其中只有 2 份会先清只读属性、只有 1 份会记日志。
/// </summary>
public sealed class BestEffortDeletionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LanDesktopDeleteTests_" + Guid.NewGuid().ToString("N"));
    private readonly Action<string, string, Exception>? _previousNotice = FileOperationRetryHelper.FailureNotice;

    public BestEffortDeletionTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        FileOperationRetryHelper.FailureNotice = _previousNotice;
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清不掉不影响结论
        }
    }

    [Fact]
    public void ReadOnlyFile_IsDeletedAfterClearingAttributes()
    {
        var path = WriteFile("note.txt");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        Assert.True(FileOperationRetryHelper.TryDeleteFile(path, "Test"));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrEmptyPath_IsAFalseNoThrow(string? path)
    {
        Assert.False(FileOperationRetryHelper.TryDeleteFile(path, "Test"));
        Assert.False(FileOperationRetryHelper.TryDeleteDirectory(path, recursive: true, "Test"));
    }

    [Fact]
    public void NonExistentFile_ReturnsFalseWithoutReporting()
    {
        var notices = CaptureNotices();
        Assert.False(FileOperationRetryHelper.TryDeleteFile(Path.Combine(_directory, "gone.txt"), "Test"));
        Assert.Empty(notices);
    }

    [Fact]
    public void NonEmptyDirectory_WithRecursiveFalse_IsKeptAndReported()
    {
        var dir = Path.Combine(_directory, "data");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "keep me.txt"), "payload");
        var notices = CaptureNotices();

        Assert.False(FileOperationRetryHelper.TryDeleteDirectory(dir, recursive: false, "DataStorage"));

        // DataStorageService 原来那一份要的就是"只删空目录"，这条钉住它没被顺手改成递归
        Assert.True(Directory.Exists(dir));
        Assert.True(File.Exists(Path.Combine(dir, "keep me.txt")));
        Assert.Single(notices);
        Assert.Equal("DataStorage", notices[0].Category);
        Assert.Contains("data", notices[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDirectory_IsDeletedRecursively()
    {
        var dir = Path.Combine(_directory, "empty");
        Directory.CreateDirectory(Path.Combine(dir, "inner"));

        Assert.True(FileOperationRetryHelper.TryDeleteDirectory(dir, recursive: true, "Test"));
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void FileHelper_LeavesDirectoryPathsAlone()
    {
        var notices = CaptureNotices();

        // 文件版只认文件：拿目录路径调它必须原样留着，也别顺手改成递归删目录
        Assert.False(FileOperationRetryHelper.TryDeleteFile(_directory, "Test"));
        Assert.True(Directory.Exists(_directory));
        Assert.Empty(notices);
    }

    [Fact]
    public void EmptyDirectory_IsDeletedWithRecursiveFalse()
    {
        var dir = Path.Combine(_directory, "empty-dir");
        Directory.CreateDirectory(dir);

        Assert.True(FileOperationRetryHelper.TryDeleteDirectory(dir, recursive: false, "Test"));
        Assert.False(Directory.Exists(dir));
    }

    private List<(string Category, string Message, Exception Error)> CaptureNotices()
    {
        var collected = new List<(string, string, Exception)>();
        FileOperationRetryHelper.FailureNotice = (category, message, error) => collected.Add((category, message, error));
        return collected;
    }

    private string WriteFile(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "content");
        return path;
    }
}
