using System.IO.Compression;

namespace LanMountainDesktop.Services.Update;

internal sealed class PlondsPayloadResolver(PlondsApplyPaths paths)
{
    public string ResolveObjectPath(ApplyPlondsFileEntry file)
    {
        var candidates = new List<string>();
        AddPathCandidates(candidates, file.ObjectPath);
        AddPathCandidates(candidates, file.ObjectKey);
        AddPathCandidates(candidates, file.ArchivePath);
        AddPathCandidates(candidates, file.ObjectUrl);
        AddPathCandidates(candidates, file.Url);

        if (PlondsManifestParser.TryGetExpectedObjectSha512(file, out var expectedSha512) ||
            PlondsManifestParser.TryGetExpectedSha512(file, out expectedSha512))
        {
            var hashHex = Convert.ToHexString(expectedSha512).ToLowerInvariant();
            AddPathCandidates(candidates, Path.Combine(PlondsApplyPaths.PlondsObjectsDirectoryName, hashHex));
            if (hashHex.Length > 2)
            {
                AddPathCandidates(candidates, Path.Combine(PlondsApplyPaths.PlondsObjectsDirectoryName, hashHex[..2], hashHex));
                AddPathCandidates(candidates, Path.Combine(PlondsApplyPaths.PlondsObjectsDirectoryName, hashHex[..2], hashHex[2..]));
            }

            AddPathCandidates(candidates, Path.Combine(PlondsApplyPaths.PlondsObjectsDirectoryName, $"{hashHex}.gz"));
        }

        foreach (var relativePath in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(Path.Combine(paths.IncomingRoot, relativePath));
            if (!fullPath.StartsWith(Path.GetFullPath(paths.IncomingRoot), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new FileNotFoundException($"Unable to resolve object payload for '{file.Path}'.");
    }

    public static byte[]? TryInflateGzip(byte[] payload)
    {
        try
        {
            using var input = new MemoryStream(payload, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void AddPathCandidates(ICollection<string> candidates, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            normalized = Uri.UnescapeDataString(absoluteUri.AbsolutePath);
        }

        normalized = normalized.TrimStart('/', '\\');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        normalized = normalized.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        candidates.Add(normalized);

        if (!normalized.StartsWith($"{PlondsApplyPaths.PlondsObjectsDirectoryName}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(PlondsApplyPaths.PlondsObjectsDirectoryName, normalized));
        }

        var fileName = Path.GetFileName(normalized);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            candidates.Add(Path.Combine(PlondsApplyPaths.PlondsObjectsDirectoryName, fileName));
        }
    }
}
