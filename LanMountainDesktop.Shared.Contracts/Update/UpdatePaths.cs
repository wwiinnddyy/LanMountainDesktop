namespace LanMountainDesktop.Shared.Contracts.Update;

public static class UpdatePaths
{
    private const string LauncherDirectoryName = ".launcher";
    private const string UpdateDirectoryName = "update";
    private const string IncomingDirectoryName = "incoming";
    private const string ObjectsDirectoryName = "objects";
    private const string SnapshotsDirectoryName = "snapshots";

    public static string ResolveLauncherRoot(string appBaseDirectory)
    {
        var trimmed = appBaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed);
        return string.IsNullOrWhiteSpace(parent) ? appBaseDirectory : parent;
    }

    public static string GetLauncherDataRoot(string launcherRoot)
    {
        return Path.Combine(launcherRoot, LauncherDirectoryName);
    }

    public static string GetIncomingDirectory(string launcherRoot)
    {
        return Path.Combine(launcherRoot, LauncherDirectoryName, UpdateDirectoryName, IncomingDirectoryName);
    }

    public static string GetObjectsDirectory(string launcherRoot)
    {
        return Path.Combine(GetIncomingDirectory(launcherRoot), ObjectsDirectoryName);
    }

    public static string GetSnapshotsDirectory(string launcherRoot)
    {
        return Path.Combine(launcherRoot, LauncherDirectoryName, SnapshotsDirectoryName);
    }

    public static string GetDownloadMarkerPath(string launcherRoot)
    {
        return Path.Combine(GetIncomingDirectory(launcherRoot), ".download-complete");
    }

    public static string GetPlondsFileMapName() => "plonds-filemap.json";
    public static string GetPlondsSignatureName() => "plonds-filemap.sig";
    public static string GetPlondsUpdateMetadataName() => "plonds-update.json";
    public static string GetLegacyFileMapName() => "files.json";
    public static string GetLegacySignatureName() => "files.json.sig";
    public static string GetLegacyArchiveName() => "update.zip";
    public static string GetPublicKeyFileName() => "public-key.pem";

    public static string GetPlondsFileMapPath(string launcherRoot)
        => Path.Combine(GetIncomingDirectory(launcherRoot), GetPlondsFileMapName());

    public static string GetPlondsSignaturePath(string launcherRoot)
        => Path.Combine(GetIncomingDirectory(launcherRoot), GetPlondsSignatureName());

    public static string GetPlondsUpdateMetadataPath(string launcherRoot)
        => Path.Combine(GetIncomingDirectory(launcherRoot), GetPlondsUpdateMetadataName());

    public static string GetDownloadMarkerContent(string manifestSha256, string targetVersion, int objectCount)
    {
        return $$"""
                 {
                   "manifestSha256": "{{manifestSha256}}",
                   "targetVersion": "{{targetVersion}}",
                   "objectCount": {{objectCount}},
                   "completedAt": "{{DateTimeOffset.UtcNow:O}}"
                 }
                 """;
    }
}
