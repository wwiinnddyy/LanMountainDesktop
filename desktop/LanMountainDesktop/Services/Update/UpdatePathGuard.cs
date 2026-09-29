using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Services.Update;

internal static class UpdatePathGuard
{
    public static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return normalized.TrimStart(Path.DirectorySeparatorChar);
    }

    public static void EnsurePathWithinRoot(string targetPath, string rootPath)
    {
        // 走家而不是在这里比前缀：裸 `target.StartsWith(root)` 把 C:\pkg-evil\1.zip 算进 C:\pkg 里，
        // 而这个方法的异常文案写的正是 "Path traversal detected"——那道闸原来对兄弟目录是开着的。
        if (!PathContainment.IsSameOrChild(rootPath, targetPath))
        {
            throw new InvalidOperationException($"Path traversal detected: {targetPath}");
        }
    }
}
