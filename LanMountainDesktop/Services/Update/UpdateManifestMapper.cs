using System.Runtime.InteropServices;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal static class UpdateManifestMapper
{
    public static UpdateManifest FromGitHubRelease(
        GitHubReleaseInfo release,
        PlondsUpdatePayload? plondsPayload,
        string channel,
        string platform)
    {
        if (plondsPayload is not null)
        {
            return FromPlondsPayload(plondsPayload, release, channel, platform);
        }

        return FromFullInstaller(release, channel, platform);
    }

    public static UpdateManifest FromPlondsPayload(
        PlondsUpdatePayload payload,
        GitHubReleaseInfo release,
        string channel,
        string platform)
    {
        var files = new List<UpdateFileEntry>();

        if (payload.UpdateArchiveUrl is not null)
        {
            files.Add(new UpdateFileEntry(
                Path: "update.zip",
                Action: "add",
                Sha256: payload.UpdateArchiveSha256 ?? string.Empty,
                Size: payload.UpdateArchiveSizeBytes ?? 0,
                Mode: "compressed-object",
                ObjectKey: null,
                ObjectUrl: payload.UpdateArchiveUrl,
                ArchiveSha256: null,
                Metadata: null));
        }

        var mirrors = release.Assets
            .Where(IsInstallerAsset)
            .Select(a => new UpdateMirrorAsset(
                Platform: platform,
                Url: a.BrowserDownloadUrl,
                Name: a.Name,
                Sha256: a.Sha256,
                Size: a.SizeBytes))
            .ToArray();

        var metadata = new Dictionary<string, string>
        {
            ["source"] = "github-plonds",
            ["releaseTag"] = release.TagName
        };

        return new UpdateManifest(
            DistributionId: payload.DistributionId,
            FromVersion: string.Empty,
            ToVersion: NormalizeTagVersion(release.TagName),
            Platform: platform,
            Channel: channel,
            PublishedAt: release.PublishedAt,
            Kind: UpdatePayloadKind.DeltaPlonds,
            FileMapUrl: payload.FileMapJsonUrl,
            FileMapSignatureUrl: payload.FileMapSignatureUrl,
            FileMapSha256: null,
            Files: files,
            InstallerMirrors: mirrors,
            Metadata: metadata);
    }

    public static UpdateManifest FromFullInstaller(
        GitHubReleaseInfo release,
        string channel,
        string platform)
    {
        var installerAsset = SelectPreferredInstallerAsset(release.Assets);

        var files = new List<UpdateFileEntry>();
        var mirrors = new List<UpdateMirrorAsset>();

        if (installerAsset is not null)
        {
            files.Add(new UpdateFileEntry(
                Path: installerAsset.Name,
                Action: "add",
                Sha256: installerAsset.Sha256 ?? string.Empty,
                Size: installerAsset.SizeBytes,
                Mode: "file-object",
                ObjectKey: null,
                ObjectUrl: installerAsset.BrowserDownloadUrl,
                ArchiveSha256: null,
                Metadata: null));

            foreach (var asset in release.Assets)
            {
                if (IsInstallerAsset(asset) && asset != installerAsset)
                {
                    mirrors.Add(new UpdateMirrorAsset(
                        Platform: platform,
                        Url: asset.BrowserDownloadUrl,
                        Name: asset.Name,
                        Sha256: asset.Sha256,
                        Size: asset.SizeBytes));
                }
            }
        }

        var distributionId = $"github-{release.TagName.Trim().TrimStart('v')}-{platform}";

        var metadata = new Dictionary<string, string>
        {
            ["source"] = "github-release",
            ["releaseTag"] = release.TagName
        };

        return new UpdateManifest(
            DistributionId: distributionId,
            FromVersion: string.Empty,
            ToVersion: NormalizeTagVersion(release.TagName),
            Platform: platform,
            Channel: channel,
            PublishedAt: release.PublishedAt,
            Kind: UpdatePayloadKind.FullInstaller,
            FileMapUrl: null,
            FileMapSignatureUrl: null,
            FileMapSha256: null,
            Files: files,
            InstallerMirrors: mirrors,
            Metadata: metadata);
    }

    private static string NormalizeTagVersion(string tagName)
    {
        var v = tagName.Trim();
        if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            v = v[1..];
        }

        return v;
    }

    private static bool IsInstallerAsset(GitHubReleaseAsset asset)
    {
        var name = asset.Name;
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubReleaseAsset? SelectPreferredInstallerAsset(IReadOnlyList<GitHubReleaseAsset> assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        var architectureToken = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        if (OperatingSystem.IsWindows())
        {
            return assets
                .Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            || a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => ScoreAsset(a.Name, architectureToken))
                .FirstOrDefault();
        }

        if (OperatingSystem.IsLinux())
        {
            return assets
                .Where(a => a.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase)
                            || a.Name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase)
                            || a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => ScoreAsset(a.Name, architectureToken))
                .FirstOrDefault();
        }

        if (OperatingSystem.IsMacOS())
        {
            return assets
                .Where(a => a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)
                            || a.Name.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => ScoreAsset(a.Name, architectureToken))
                .FirstOrDefault();
        }

        return null;
    }

    private static int ScoreAsset(string name, string archToken)
    {
        var score = 0;
        if (name.Contains(archToken, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (name.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || name.Contains("installer", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        return score;
    }
}
