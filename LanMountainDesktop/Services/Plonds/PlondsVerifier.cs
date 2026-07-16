using System.Security.Cryptography;

namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsVerifier
{
    public async Task VerifyFileAsync(
        string filePath,
        IReadOnlyDictionary<string, string>? checksums,
        IEnumerable<string> checksumKeys,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("PLONDS package was not downloaded.", filePath);
        }

        var checksum = FindChecksum(checksums, checksumKeys);
        if (checksum is null)
        {
            // Some published manifests only list per-file hashes, not package zip hashes.
            // Allow install to proceed after a successful HTTP download when zip checksum is absent.
            AppLogger.Warn("PLONDS.Verify", $"No package checksum declared for keys [{string.Join(", ", checksumKeys)}]; skipping zip hash verification.");
            return;
        }

        var (algorithm, expectedHash) = ParseChecksum(checksum);
        var actualHash = await ComputeHashAsync(filePath, algorithm, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"PLONDS package checksum mismatch. Expected {algorithm}:{expectedHash}, actual {algorithm}:{actualHash}.");
        }
    }

    private static string? FindChecksum(
        IReadOnlyDictionary<string, string>? checksums,
        IEnumerable<string> checksumKeys)
    {
        if (checksums is null || checksums.Count == 0)
        {
            return null;
        }

        foreach (var key in checksumKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (checksums.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var match = checksums.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value;
            }
        }

        return null;
    }

    private static (string Algorithm, string Hash) ParseChecksum(string checksum)
    {
        var normalized = checksum.Trim();
        var separatorIndex = normalized.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            var algorithm = normalized[..separatorIndex].Trim().ToLowerInvariant();
            var hash = NormalizeHash(normalized[(separatorIndex + 1)..]);
            if (algorithm is "md5" or "sha256" && hash.Length > 0)
            {
                return (algorithm, hash);
            }
        }

        var inferredHash = NormalizeHash(normalized);
        return inferredHash.Length switch
        {
            32 => ("md5", inferredHash),
            64 => ("sha256", inferredHash),
            _ => throw new InvalidDataException($"Unsupported PLONDS checksum format: {checksum}")
        };
    }

    private static async Task<string> ComputeHashAsync(
        string filePath,
        string algorithm,
        CancellationToken cancellationToken)
    {
        using HashAlgorithm hasher = algorithm switch
        {
            "md5" => MD5.Create(),
            "sha256" => SHA256.Create(),
            _ => throw new InvalidDataException($"Unsupported PLONDS checksum algorithm: {algorithm}")
        };

        await using var stream = File.OpenRead(filePath);
        var hash = await hasher.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeHash(string value)
    {
        return value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }
}
