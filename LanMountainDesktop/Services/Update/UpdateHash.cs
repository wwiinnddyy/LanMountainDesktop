using System.Security.Cryptography;

namespace LanMountainDesktop.Services.Update;

internal static class UpdateHash
{
    public static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static byte[] ComputeSha512(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return SHA512.HashData(stream);
    }

    public static bool TryParseHashBytes(string? rawHash, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(rawHash))
        {
            return false;
        }

        var normalized = rawHash.Trim();
        var separator = normalized.IndexOf(':');
        if (separator >= 0 && separator < normalized.Length - 1)
        {
            normalized = normalized[(separator + 1)..].Trim();
        }

        var compact = normalized.Replace("-", string.Empty);
        if (compact.Length > 0 && compact.Length % 2 == 0 && IsHexString(compact))
        {
            try
            {
                bytes = Convert.FromHexString(compact);
                return true;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            bytes = Convert.FromBase64String(normalized);
            return bytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static string NormalizeHashText(string hash)
    {
        var normalized = hash.Trim();
        var separator = normalized.IndexOf(':');
        if (separator >= 0 && separator < normalized.Length - 1)
        {
            normalized = normalized[(separator + 1)..];
        }

        return normalized.Replace("-", string.Empty).Trim().ToLowerInvariant();
    }

    private static bool IsHexString(string value)
    {
        foreach (var ch in value)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
