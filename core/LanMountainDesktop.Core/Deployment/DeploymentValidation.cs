using System.Security.Cryptography;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Shared.Contracts.Deployment;

internal sealed record DeploymentFileHash(string Path, string Action, string Hash, string Algorithm, long Size);

internal static class DeploymentValidation
{
    internal static void Verify(string directory, IEnumerable<DeploymentFileHash> files, CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.Combine(directory, DeploymentLayout.GetHostExecutableName())))
            throw new InvalidDataException("The deployment does not contain the host executable.");

        var count = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DeploymentLayout.IsDeploymentMarker(file.Path))
                continue;
            var path = Path.GetFullPath(Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathContainment.IsSameOrChild(directory, path))
                throw new InvalidDataException($"Deployment file escapes its root: {file.Path}");
            if (string.Equals(file.Action, "delete", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(path))
                    throw new InvalidDataException($"Deleted deployment file still exists: {file.Path}");
                continue;
            }
            if (!File.Exists(path))
                throw new FileNotFoundException($"Deployment file is missing: {file.Path}", path);
            if (string.IsNullOrWhiteSpace(file.Hash))
                throw new InvalidDataException($"Deployment file checksum is missing: {file.Path}");
            using var stream = File.OpenRead(path);
            var algorithm = file.Algorithm.Trim().ToLowerInvariant();
            var bytes = algorithm switch
            {
                "sha256" => SHA256.HashData(stream),
                "md5" => MD5.HashData(stream),
                _ => throw new InvalidDataException($"Unsupported deployment checksum: {algorithm}")
            };
            if (!string.Equals(Convert.ToHexString(bytes), file.Hash.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Deployment file checksum mismatch: {file.Path}");
            if (file.Size > 0 && stream.Length != file.Size)
                throw new InvalidDataException($"Deployment file size mismatch: {file.Path}");
            count++;
        }
        if (count == 0)
            throw new InvalidDataException("The deployment manifest contains no verifiable files.");
    }
}
