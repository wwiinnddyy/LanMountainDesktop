using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;

namespace LanMountainDesktop.Services;

public sealed record CurrentUserProfileSnapshot(
    string DisplayName,
    Bitmap? AvatarBitmap,
    string FallbackMonogram,
    bool IsPlaceholder);

public interface ICurrentUserProfileService
{
    CurrentUserProfileSnapshot GetCurrentProfile();
}

internal sealed class CurrentUserProfileService : ICurrentUserProfileService, IDisposable
{
    private readonly object _gate = new();
    private CurrentUserProfileSnapshot? _cachedSnapshot;
    private Bitmap? _cachedAvatarBitmap;

    public CurrentUserProfileSnapshot GetCurrentProfile()
    {
        lock (_gate)
        {
            if (_cachedSnapshot is not null)
            {
                return _cachedSnapshot;
            }

            var displayName = ResolveDisplayName();
            _cachedAvatarBitmap = TryLoadSystemAvatarBitmap();
            _cachedSnapshot = new CurrentUserProfileSnapshot(
                displayName,
                _cachedAvatarBitmap,
                BuildMonogram(displayName),
                _cachedAvatarBitmap is null);
            return _cachedSnapshot;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cachedSnapshot = null;
            _cachedAvatarBitmap?.Dispose();
            _cachedAvatarBitmap = null;
        }
    }

    private static string ResolveDisplayName()
    {
        var userName = Environment.UserName?.Trim();
        return string.IsNullOrWhiteSpace(userName) ? "User" : userName;
    }

    private static Bitmap? TryLoadSystemAvatarBitmap()
    {
        foreach (var path in EnumerateAvatarCandidates())
        {
            try
            {
                using var stream = File.OpenRead(path);
                return new Bitmap(stream);
            }
            catch
            {
                // Ignore unreadable avatar files and continue with the next candidate.
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateAvatarCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateDirectoryCandidates(
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "Microsoft",
                         "Windows",
                         "AccountPictures")))
        {
            if (seen.Add(path))
            {
                yield return path;
            }
        }

        foreach (var path in EnumerateDirectoryCandidates(
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Microsoft",
                         "Windows",
                         "AccountPictures")))
        {
            if (seen.Add(path))
            {
                yield return path;
            }
        }

        var commonPicturesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "User Account Pictures");

        foreach (var fileName in new[]
                 {
                     "user-448.png",
                     "user-240.png",
                     "user-192.png",
                     "user-96.png",
                     "user-64.png",
                     "user-48.png",
                     "user.png"
                 })
        {
            var path = Path.Combine(commonPicturesDirectory, fileName);
            if (File.Exists(path) && seen.Add(path))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoryCandidates(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            yield break;
        }

        var files = Directory.EnumerateFiles(directoryPath)
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Length);

        foreach (var file in files)
        {
            yield return file.FullName;
        }
    }

    private static string BuildMonogram(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        var letters = text
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part[0])
            .Take(2)
            .ToArray();

        if (letters.Length == 0)
        {
            return "?";
        }

        return new string(letters).ToUpperInvariant();
    }
}

internal static class HostCurrentUserProfileProvider
{
    private static readonly object Gate = new();
    private static ICurrentUserProfileService? _instance;

    public static ICurrentUserProfileService GetOrCreate()
    {
        lock (Gate)
        {
            return _instance ??= new CurrentUserProfileService();
        }
    }
}
