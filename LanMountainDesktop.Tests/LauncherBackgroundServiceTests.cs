using Avalonia;
using LanMountainDesktop.Launcher.Shell;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherBackgroundServiceTests : IDisposable
{
    private const string RedPng1x1 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC";

    private const string BluePng2x2 =
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAASSURBVBhXY2Bg+P8fgsHE//8AP9IH+WMJIRIAAAAASUVORK5CYII=";

    private const string GreenJpeg1x1 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDiqKKK+aPjz//Z";

    private readonly string _tempDirectory;
    private readonly string _launcherDataDirectory;
    private static readonly object AvaloniaGate = new();
    private static bool _avaloniaInitialized;

    public LauncherBackgroundServiceTests()
    {
        EnsureAvaloniaInitialized();

        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LanMountainDesktop.BackgroundImageTests",
            Guid.NewGuid().ToString("N"));
        _launcherDataDirectory = Path.Combine(_tempDirectory, ".Launcher");
        Directory.CreateDirectory(_launcherDataDirectory);
        LauncherBackgroundService.LauncherDataDirectoryOverride = _launcherDataDirectory;
        LauncherBackgroundService.ClearCache();
    }

    private static void EnsureAvaloniaInitialized()
    {
        lock (AvaloniaGate)
        {
            if (_avaloniaInitialized)
            {
                return;
            }

            if (Application.Current is null)
            {
                AppBuilder
                    .Configure<Application>()
                    .UsePlatformDetect()
                    .SetupWithoutStarting();
            }

            _avaloniaInitialized = true;
        }
    }

    [Fact]
    public void SaveBackgroundImage_CopiesSelectedImageToLauncherDataDirectory()
    {
        var sourcePath = WriteImage("selected.png", RedPng1x1);

        var result = LauncherBackgroundService.SaveBackgroundImage(sourcePath);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(Path.Combine(_launcherDataDirectory, "Launcher Picture.png"), result.FilePath);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(result.FilePath));
    }

    [Fact]
    public void SaveBackgroundImage_ReplacesPreviousManagedExtension()
    {
        var pngSourcePath = WriteImage("first.png", RedPng1x1);
        var jpegSourcePath = WriteImage("second.jpg", GreenJpeg1x1);

        var firstResult = LauncherBackgroundService.SaveBackgroundImage(pngSourcePath);
        var secondResult = LauncherBackgroundService.SaveBackgroundImage(jpegSourcePath);

        Assert.True(firstResult.IsSuccess, firstResult.ErrorMessage);
        Assert.True(secondResult.IsSuccess, secondResult.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(_launcherDataDirectory, "Launcher Picture.png")));
        Assert.True(File.Exists(Path.Combine(_launcherDataDirectory, "Launcher Picture.jpg")));
    }

    [Fact]
    public void LoadBackgroundImage_AcceptsNonSevenByFiveImage()
    {
        var sourcePath = WriteImage("square.png", RedPng1x1);

        var saveResult = LauncherBackgroundService.SaveBackgroundImage(sourcePath);
        var imageInfo = LauncherBackgroundService.LoadBackgroundImage();

        Assert.True(saveResult.IsSuccess, saveResult.ErrorMessage);
        Assert.True(imageInfo.IsValid, imageInfo.ErrorMessage);
        Assert.Equal(1, imageInfo.Width);
        Assert.Equal(1, imageInfo.Height);
    }

    [Theory]
    [InlineData("oversized.png", InvalidImageKind.Oversized)]
    [InlineData("unknown.txt", InvalidImageKind.UnknownExtension)]
    [InlineData("broken.png", InvalidImageKind.BrokenImage)]
    public void SaveBackgroundImage_WhenInvalid_DoesNotOverwriteExistingImage(
        string invalidFileName,
        InvalidImageKind invalidImageKind)
    {
        var existingPath = WriteImage("existing.png", RedPng1x1);
        var existingResult = LauncherBackgroundService.SaveBackgroundImage(existingPath);
        var managedPath = existingResult.FilePath!;
        var originalBytes = File.ReadAllBytes(managedPath);
        var invalidPath = WriteInvalidFile(invalidFileName, invalidImageKind);

        var invalidResult = LauncherBackgroundService.SaveBackgroundImage(invalidPath);

        Assert.False(invalidResult.IsSuccess);
        Assert.True(File.Exists(managedPath));
        Assert.Equal(originalBytes, File.ReadAllBytes(managedPath));
    }

    [Fact]
    public void LoadBackgroundImage_WhenFileChangesAtSamePath_RefreshesCachedBitmap()
    {
        var sourcePath = WriteImage("source.png", RedPng1x1);
        var saveResult = LauncherBackgroundService.SaveBackgroundImage(sourcePath);
        Assert.True(saveResult.IsSuccess, saveResult.ErrorMessage);

        var firstLoad = LauncherBackgroundService.LoadBackgroundImage();
        Assert.True(firstLoad.IsValid, firstLoad.ErrorMessage);
        Assert.Equal(1, firstLoad.Width);

        var managedPath = saveResult.FilePath!;
        File.WriteAllBytes(managedPath, Convert.FromBase64String(BluePng2x2));
        File.SetLastWriteTimeUtc(managedPath, DateTime.UtcNow.AddSeconds(2));

        var secondLoad = LauncherBackgroundService.LoadBackgroundImage();

        Assert.True(secondLoad.IsValid, secondLoad.ErrorMessage);
        Assert.Equal(2, secondLoad.Width);
        Assert.Equal(2, secondLoad.Height);
    }

    public void Dispose()
    {
        LauncherBackgroundService.ClearCache();
        LauncherBackgroundService.LauncherDataDirectoryOverride = null;

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string WriteImage(string fileName, string base64)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
        return path;
    }

    private string WriteInvalidFile(string fileName, InvalidImageKind kind)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        var bytes = kind switch
        {
            InvalidImageKind.Oversized => new byte[(10 * 1024 * 1024) + 1],
            InvalidImageKind.UnknownExtension => Convert.FromBase64String(RedPng1x1),
            InvalidImageKind.BrokenImage => "not an image"u8.ToArray(),
            _ => []
        };

        File.WriteAllBytes(path, bytes);
        return path;
    }

    public enum InvalidImageKind
    {
        Oversized,
        UnknownExtension,
        BrokenImage
    }
}
