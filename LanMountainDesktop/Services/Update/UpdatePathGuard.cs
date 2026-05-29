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
        var fullTarget = Path.GetFullPath(targetPath);
        var fullRoot = Path.GetFullPath(rootPath);
        if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path traversal detected: {targetPath}");
        }
    }
}
