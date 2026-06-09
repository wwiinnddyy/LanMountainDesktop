namespace LanDesktopPLONDS.Installer.Services;

public static class InstallerPathGuard
{
    public const string ApplicationDirectoryName = "LanMountainDesktop";

    public static string GetDefaultInstallPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, "Programs", ApplicationDirectoryName);
    }

    public static string GetInstallPathForSelectedFolder(string selectedFolder)
    {
        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            throw new ArgumentException("Selected folder is required.", nameof(selectedFolder));
        }

        var fullPath = Path.GetFullPath(selectedFolder.Trim());
        var root = Path.GetPathRoot(fullPath);
        var trimmedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var trimmedRoot = root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var basePath = string.Equals(trimmedPath, trimmedRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : trimmedPath;
        var selectedName = Path.GetFileName(trimmedPath);
        var installPath = string.Equals(selectedName, ApplicationDirectoryName, StringComparison.OrdinalIgnoreCase)
            ? trimmedPath
            : Path.Combine(basePath, ApplicationDirectoryName);

        return NormalizeInstallPath(installPath);
    }

    public static string NormalizeInstallPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Installation path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim());
        ValidateInstallPath(fullPath);
        return fullPath;
    }

    public static void ValidateInstallPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Installation path is required.");
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Choose a folder instead of a drive root.");
        }

        var blockedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows",
            "System32",
            "SysWOW64",
            "Program Files",
            "Program Files (x86)",
            "Users"
        };
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (blockedNames.Contains(name))
        {
            throw new InvalidOperationException("Choose a dedicated application folder.");
        }
    }

    public static void EnsureUsableInstallPath(string path, long requiredBytes)
    {
        var fullPath = NormalizeInstallPath(path);
        var directory = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath)
            : Directory.CreateDirectory(fullPath);

        var testPath = Path.Combine(directory.FullName, $".write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(testPath, string.Empty);
        }
        finally
        {
            if (File.Exists(testPath))
            {
                File.Delete(testPath);
            }
        }

        var drive = new DriveInfo(directory.Root.FullName);
        if (drive.AvailableFreeSpace > 0 && drive.AvailableFreeSpace < requiredBytes)
        {
            throw new InvalidOperationException("The selected drive does not have enough free space.");
        }
    }

    public static void EnsureChildPath(string parent, string child)
    {
        if (!IsSameOrChildPath(parent, child))
        {
            throw new InvalidDataException($"Path escapes the expected root: {child}");
        }
    }

    public static bool IsSameOrChildPath(string parent, string child)
    {
        var resolvedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolvedChild = Path.GetFullPath(child);
        return string.Equals(
                   resolvedParent,
                   resolvedChild.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase)
               || resolvedChild.StartsWith(resolvedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || resolvedChild.StartsWith(resolvedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("Package entry path is empty.");
        }

        var normalized = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) || normalized.Split(Path.DirectorySeparatorChar).Contains(".."))
        {
            throw new InvalidDataException($"Package entry path is invalid: {relativePath}");
        }

        return normalized;
    }
}
