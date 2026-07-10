using Avalonia.Media.Imaging;
using SkiaSharp;

namespace LanMountainDesktop.Launcher.Shell;

internal static class LauncherBackgroundService
{
    private const string PictureFileName = "Launcher Picture";
    private const long MaxFileSize = 10 * 1024 * 1024;

    private static readonly string[] SupportedExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp"
    ];

    private static Bitmap? _cachedBitmap;
    private static string? _cachedPath;
    private static long _cachedLength;
    private static DateTime _cachedLastWriteTimeUtc;
    private static int _cachedWidth;
    private static int _cachedHeight;

    internal static string? LauncherDataDirectoryOverride { get; set; }

    public record BackgroundImageInfo
    {
        public required bool Exists { get; init; }
        public required bool IsValid { get; init; }
        public string? FilePath { get; init; }
        public Bitmap? Bitmap { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public double AspectRatio { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public record BackgroundImageMutationResult
    {
        public required bool IsSuccess { get; init; }
        public string? FilePath { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public static BackgroundImageInfo LoadBackgroundImage()
    {
        try
        {
            var launcherPath = ResolveLauncherDataPath();
            var imagePath = FindImageFile(launcherPath);
            if (imagePath is null)
            {
                return new BackgroundImageInfo
                {
                    Exists = false,
                    IsValid = false,
                    ErrorMessage = "No launcher background image was found."
                };
            }

            var fileInfo = new FileInfo(imagePath);
            if (fileInfo.Length > MaxFileSize)
            {
                return new BackgroundImageInfo
                {
                    Exists = true,
                    IsValid = false,
                    FilePath = imagePath,
                    ErrorMessage = $"Image file is too large ({fileInfo.Length / 1024 / 1024}MB > 10MB)."
                };
            }

            if (IsCacheCurrent(imagePath, fileInfo))
            {
                return new BackgroundImageInfo
                {
                    Exists = true,
                    IsValid = true,
                    FilePath = imagePath,
                    Bitmap = _cachedBitmap,
                    Width = _cachedWidth,
                    Height = _cachedHeight,
                    AspectRatio = (double)_cachedWidth / _cachedHeight
                };
            }

            DisposeCache();

            if (!TryInspectImage(imagePath, out var decodedWidth, out var decodedHeight, out var decodeError))
            {
                return new BackgroundImageInfo
                {
                    Exists = true,
                    IsValid = false,
                    FilePath = imagePath,
                    ErrorMessage = $"Image could not be decoded: {decodeError}"
                };
            }

            Bitmap bitmap;
            try
            {
                // Decode from a stream instead of the path-based constructor. Avalonia/Skia may
                // retain a path-backed image source, which can return stale pixels when a file is
                // replaced in place.
                using var imageStream = new FileStream(
                    imagePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                bitmap = new Bitmap(imageStream);
            }
            catch (Exception ex)
            {
                return new BackgroundImageInfo
                {
                    Exists = true,
                    IsValid = false,
                    FilePath = imagePath,
                    ErrorMessage = $"Image could not be decoded: {ex.Message}"
                };
            }

            var width = decodedWidth;
            var height = decodedHeight;
            var aspectRatio = height == 0 ? 0d : (double)width / height;

            _cachedBitmap = bitmap;
            _cachedPath = imagePath;
            _cachedLength = fileInfo.Length;
            _cachedLastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            _cachedWidth = width;
            _cachedHeight = height;

            Logger.Info($"[LauncherBackground] Background image loaded: {imagePath} ({width}x{height}).");

            return new BackgroundImageInfo
            {
                Exists = true,
                IsValid = true,
                FilePath = imagePath,
                Bitmap = bitmap,
                Width = width,
                Height = height,
                AspectRatio = aspectRatio
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"[LauncherBackground] Failed to load background image: {ex.Message}");
            return new BackgroundImageInfo
            {
                Exists = false,
                IsValid = false,
                ErrorMessage = $"Load failed: {ex.Message}"
            };
        }
    }

    public static BackgroundImageMutationResult SaveBackgroundImage(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return FailMutation("No image file was selected.");
            }

            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullSourcePath))
            {
                return FailMutation("The selected image file does not exist.");
            }

            var extension = NormalizeExtension(Path.GetExtension(fullSourcePath));
            if (!IsSupportedExtension(extension))
            {
                return FailMutation("The selected image format is not supported.");
            }

            var sourceInfo = new FileInfo(fullSourcePath);
            if (sourceInfo.Length > MaxFileSize)
            {
                return FailMutation($"Image file is too large ({sourceInfo.Length / 1024 / 1024}MB > 10MB).");
            }

            if (!TryInspectImage(fullSourcePath, out _, out _, out var decodeError))
            {
                return FailMutation($"The selected image could not be decoded: {decodeError}");
            }

            var launcherPath = ResolveLauncherDataPath();
            Directory.CreateDirectory(launcherPath);

            var destinationPath = Path.Combine(launcherPath, PictureFileName + extension);
            var tempPath = Path.Combine(launcherPath, $".{PictureFileName}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.Copy(fullSourcePath, tempPath, overwrite: true);
                ClearCache();
                File.Move(tempPath, destinationPath, overwrite: true);
                DeleteManagedImageFiles(launcherPath, destinationPath);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }

            ClearCache();

            Logger.Info($"[LauncherBackground] Background image saved: {destinationPath}.");
            return new BackgroundImageMutationResult
            {
                IsSuccess = true,
                FilePath = destinationPath
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"[LauncherBackground] Failed to save background image: {ex.Message}");
            return FailMutation($"Save failed: {ex.Message}");
        }
    }

    public static BackgroundImageMutationResult ClearBackgroundImage()
    {
        try
        {
            var launcherPath = ResolveLauncherDataPath();
            ClearCache();
            DeleteManagedImageFiles(launcherPath, exceptPath: null);

            Logger.Info("[LauncherBackground] Background image cleared.");
            return new BackgroundImageMutationResult
            {
                IsSuccess = true
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"[LauncherBackground] Failed to clear background image: {ex.Message}");
            return FailMutation($"Clear failed: {ex.Message}");
        }
    }

    public static void ClearCache()
    {
        DisposeCache();
        _cachedPath = null;
        _cachedLength = 0;
        _cachedLastWriteTimeUtc = DateTime.MinValue;
        _cachedWidth = 0;
        _cachedHeight = 0;
    }

    internal static string? FindManagedImageFile()
    {
        return FindImageFile(ResolveLauncherDataPath());
    }

    internal static IReadOnlyList<string> GetSupportedExtensions() => SupportedExtensions;

    private static BackgroundImageMutationResult FailMutation(string message)
    {
        return new BackgroundImageMutationResult
        {
            IsSuccess = false,
            ErrorMessage = message
        };
    }

    private static bool IsCacheCurrent(string imagePath, FileInfo fileInfo)
    {
        return _cachedBitmap is not null &&
               string.Equals(_cachedPath, imagePath, StringComparison.OrdinalIgnoreCase) &&
               _cachedLength == fileInfo.Length &&
               _cachedLastWriteTimeUtc == fileInfo.LastWriteTimeUtc;
    }

    private static bool TryInspectImage(
        string path,
        out int width,
        out int height,
        out string? error)
    {
        width = 0;
        height = 0;
        error = null;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                error = "The file is not a recognized image.";
                return false;
            }

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0)
            {
                error = "The image has invalid dimensions.";
                return false;
            }

            using var pixels = new SKBitmap(info);
            var result = codec.GetPixels(info, pixels.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                error = $"The decoder returned {result}.";
                return false;
            }

            width = info.Width;
            height = info.Height;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? FindImageFile(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var extension in SupportedExtensions)
        {
            var path = Path.Combine(directory, PictureFileName + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }

        foreach (var file in Directory.GetFiles(directory, PictureFileName + ".*"))
        {
            if (IsSupportedExtension(Path.GetExtension(file)))
            {
                return file;
            }
        }

        return null;
    }

    private static void DeleteManagedImageFiles(string directory, string? exceptPath)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, PictureFileName + ".*"))
        {
            if (!IsSupportedExtension(Path.GetExtension(file)))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(exceptPath) &&
                string.Equals(Path.GetFullPath(file), Path.GetFullPath(exceptPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteFile(file);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[LauncherBackground] Failed to delete '{path}': {ex.Message}");
        }
    }

    private static string NormalizeExtension(string? extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().ToLowerInvariant();
    }

    private static bool IsSupportedExtension(string? extension)
    {
        var normalized = NormalizeExtension(extension);
        return SupportedExtensions.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveLauncherDataPath()
    {
        if (!string.IsNullOrWhiteSpace(LauncherDataDirectoryOverride))
        {
            return Path.GetFullPath(LauncherDataDirectoryOverride);
        }

        var resolver = new DataLocationResolver(AppContext.BaseDirectory);
        return resolver.ResolveLauncherDataPath();
    }

    private static void DisposeCache()
    {
        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
    }
}
