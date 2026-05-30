namespace Plonds.Core.Publishing;

public static class PlondsCommitAnalyzer
{
    private static readonly string[] SourceDirectories =
    [
        "LanMountainDesktop/",
        "LanMountainDesktop.Launcher/",
        "LanMountainDesktop.Shared.Contracts/",
        "LanMountainDesktop.PluginSdk/",
        "LanMountainDesktop.Appearance/",
        "LanMountainDesktop.Settings.Core/",
        "LanMountainDesktop.ComponentSystem/"
    ];

    private static readonly (string Prefix, string[] Artifacts)[] SourceToArtifactMappings =
    [
        ("LanMountainDesktop/", ["LanMountainDesktop.dll", "LanMountainDesktop.exe"]),
        ("LanMountainDesktop.Launcher/", ["LanMountainDesktop.Launcher.exe", "LanMountainDesktop.Launcher.dll"]),
        ("LanMountainDesktop.Shared.Contracts/", ["LanMountainDesktop.Shared.Contracts.dll"]),
        ("LanMountainDesktop.PluginSdk/", ["LanMountainDesktop.PluginSdk.dll"]),
        ("LanMountainDesktop.Appearance/", ["LanMountainDesktop.Appearance.dll"]),
        ("LanMountainDesktop.Settings.Core/", ["LanMountainDesktop.Settings.Core.dll"]),
        ("LanMountainDesktop.ComponentSystem/", ["LanMountainDesktop.ComponentSystem.dll"])
    ];

    private static readonly string[] SourceCodeExtensions =
    [
        ".cs", ".axaml", ".xaml", ".csproj"
    ];

    public static HashSet<string> GetChangedSourceFiles(string baselineTag, string currentTag)
    {
        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var start = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"log --name-only --pretty=format: {baselineTag}..{currentTag}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (start is null)
        {
            return changedFiles;
        }

        var output = start.StandardOutput.ReadToEnd();
        start.WaitForExit();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (!IsSourceDirectoryFile(trimmed))
            {
                continue;
            }

            changedFiles.Add(trimmed);
        }

        return changedFiles;
    }

    public static HashSet<string> MapSourceFilesToArtifacts(IReadOnlySet<string> sourceFiles)
    {
        var artifacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasUnmappedChanges = false;

        foreach (var sourceFile in sourceFiles)
        {
            var normalized = sourceFile.Replace('\\', '/');
            var mapped = false;

            foreach (var (prefix, artifactList) in SourceToArtifactMappings)
            {
                if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsSourceCodeFile(normalized) && !IsConfigFile(normalized))
                {
                    continue;
                }

                foreach (var artifact in artifactList)
                {
                    artifacts.Add(artifact);
                }

                mapped = true;
                break;
            }

            if (!mapped && IsConfigFile(normalized))
            {
                var artifactPath = MapConfigToArtifact(normalized);
                if (artifactPath is not null)
                {
                    artifacts.Add(artifactPath);
                    mapped = true;
                }
            }

            if (!mapped)
            {
                hasUnmappedChanges = true;
            }
        }

        if (hasUnmappedChanges)
        {
            foreach (var (_, artifactList) in SourceToArtifactMappings)
            {
                foreach (var artifact in artifactList)
                {
                    artifacts.Add(artifact);
                }
            }
        }

        return artifacts;
    }

    public static bool IsSourceDirectoryFile(string path)
    {
        var normalized = path.Replace('\\', '/');
        return SourceDirectories.Any(d => normalized.StartsWith(d, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSourceCodeFile(string path)
    {
        var ext = Path.GetExtension(path);
        return SourceCodeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsConfigFile(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase)
               || string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string? MapConfigToArtifact(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        return fileName;
    }
}
