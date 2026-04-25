using Avalonia.Media.Imaging;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 启动器背景图片服务
/// </summary>
internal static class LauncherBackgroundService
{
    private const string PictureFileName = "Launcher Picture";
    private const long MaxFileSize = 10 * 1024 * 1024; // 10MB
    private const double WindowAspectRatio = 7.0 / 5.0; // 700:500
    private const double AspectRatioTolerance = 0.15; // 15% 误差

    private static Bitmap? _cachedBitmap;
    private static string? _cachedPath;

    /// <summary>
    /// 背景图片信息
    /// </summary>
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

    /// <summary>
    /// 加载背景图片
    /// </summary>
    public static BackgroundImageInfo LoadBackgroundImage()
    {
        try
        {
            var resolver = new DataLocationResolver(AppContext.BaseDirectory);
            var launcherPath = resolver.ResolveLauncherDataPath();

            // 查找图片文件
            var imagePath = FindImageFile(launcherPath);
            if (imagePath == null)
            {
                return new BackgroundImageInfo
                {
                    Exists = false,
                    IsValid = false,
                    ErrorMessage = "未找到背景图片文件"
                };
            }

            // 检查文件大小
            var fileInfo = new FileInfo(imagePath);
            if (fileInfo.Length > MaxFileSize)
            {
                return new BackgroundImageInfo
                {
                    Exists = true,
                    IsValid = false,
                    FilePath = imagePath,
                    ErrorMessage = $"图片文件过大 ({fileInfo.Length / 1024 / 1024}MB > 10MB)"
                };
            }

            // 使用缓存
            if (_cachedBitmap != null && _cachedPath == imagePath)
            {
                return new BackgroundImageInfo
                {
                    Exists = true,
                    IsValid = true,
                    FilePath = imagePath,
                    Bitmap = _cachedBitmap,
                    Width = _cachedBitmap.PixelSize.Width,
                    Height = _cachedBitmap.PixelSize.Height,
                    AspectRatio = (double)_cachedBitmap.PixelSize.Width / _cachedBitmap.PixelSize.Height
                };
            }

            // 加载图片
            var bitmap = new Bitmap(imagePath);
            var width = bitmap.PixelSize.Width;
            var height = bitmap.PixelSize.Height;
            var aspectRatio = (double)width / height;

            // 校验比例
            var ratioDiff = Math.Abs(aspectRatio - WindowAspectRatio) / WindowAspectRatio;
            if (ratioDiff > AspectRatioTolerance)
            {
                bitmap.Dispose();
                return new BackgroundImageInfo
                {
                    Exists = true,
                    IsValid = false,
                    FilePath = imagePath,
                    Width = width,
                    Height = height,
                    AspectRatio = aspectRatio,
                    ErrorMessage = $"图片比例不符合要求 ({aspectRatio:F2}，需要接近 {WindowAspectRatio:F2})"
                };
            }

            // 缓存图片
            _cachedBitmap = bitmap;
            _cachedPath = imagePath;

            Logger.Info($"[LauncherBackground] 背景图片加载成功: {imagePath} ({width}x{height}, 比例: {aspectRatio:F2})");

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
            Logger.Warn($"[LauncherBackground] 加载背景图片失败: {ex.Message}");
            return new BackgroundImageInfo
            {
                Exists = false,
                IsValid = false,
                ErrorMessage = $"加载失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 查找图片文件
    /// </summary>
    private static string? FindImageFile(string directory)
    {
        var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

        foreach (var ext in extensions)
        {
            var path = Path.Combine(directory, PictureFileName + ext);
            if (File.Exists(path))
            {
                return path;
            }
        }

        // 也尝试不带扩展名的匹配（如果文件本身就有扩展名）
        var files = Directory.GetFiles(directory, PictureFileName + ".*");
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (extensions.Contains(ext))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>
    /// 清除缓存
    /// </summary>
    public static void ClearCache()
    {
        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
        _cachedPath = null;
    }
}
