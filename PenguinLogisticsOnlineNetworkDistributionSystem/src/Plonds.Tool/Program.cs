using Plonds.Core.Publishing;

return await PlondsCli.RunAsync(args);

internal static class PlondsCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].Trim().ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());

        try
        {
            switch (command)
            {
                case "build-delta":
                    RunBuildDelta(options);
                    return 0;
                case "build-delta-from-commits":
                    RunBuildDeltaFromCommits(options);
                    return 0;
                case "publish-s3":
                    return await RunPublishS3Async(options).ConfigureAwait(false);
                case "pack-payload":
                    RunPackPayload(options);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void RunBuildDelta(Dictionary<string, string> options)
    {
        var builder = new PlondsDeltaBuilder();
        var result = builder.Build(new PlondsDeltaBuildOptions(
            Platform: Require(options, "platform"),
            CurrentVersion: Require(options, "current-version"),
            CurrentPayloadZip: Require(options, "current-zip"),
            OutputRoot: Require(options, "output-dir"),
            Channel: Get(options, "channel", "stable") ?? "stable",
            BaselineVersion: Get(options, "baseline-version"),
            BaselinePayloadZip: Get(options, "baseline-zip"),
            LauncherRelativePath: Get(options, "launcher-path", "LanMountainDesktop.Launcher.exe") ?? "LanMountainDesktop.Launcher.exe",
            HashAlgorithm: Get(options, "hash-algorithm", "sha256") ?? "sha256"));

        Console.WriteLine($"Built PLONDS delta for {result.Platform}:");
        Console.WriteLine($"  IsFullUpdate:          {result.IsFullUpdate}");
        Console.WriteLine($"  RequiresCleanInstall:  {result.RequiresCleanInstall}");
        Console.WriteLine($"  ChangedZip:            {result.ChangedZipPath}");
        Console.WriteLine($"  Manifest:              {result.ManifestPath}");
    }

    private static void RunBuildDeltaFromCommits(Dictionary<string, string> options)
    {
        var builder = new PlondsCommitDeltaBuilder();
        var result = builder.Build(new PlondsCommitDeltaBuildOptions(
            Platform: Require(options, "platform"),
            CurrentVersion: Require(options, "current-version"),
            CurrentPayloadZip: Require(options, "current-zip"),
            OutputRoot: Require(options, "output-dir"),
            Channel: Require(options, "channel"),
            BaselineTag: Require(options, "baseline-tag"),
            CurrentTag: Require(options, "current-tag"),
            HashAlgorithm: Get(options, "hash-algorithm", "sha256") ?? "sha256",
            SourceDirs: Get(options, "source-dirs"),
            FallbackBaselineZip: Get(options, "fallback-zip"),
            LauncherRelativePath: Get(options, "launcher-path", "LanMountainDesktop.Launcher.exe") ?? "LanMountainDesktop.Launcher.exe"));

        Console.WriteLine($"Built PLONDS commit-delta for {result.Platform}:");
        Console.WriteLine($"  IsFullUpdate:          {result.IsFullUpdate}");
        Console.WriteLine($"  RequiresCleanInstall:  {result.RequiresCleanInstall}");
        Console.WriteLine($"  ChangedZip:            {result.ChangedZipPath}");
        Console.WriteLine($"  Manifest:              {result.ManifestPath}");
    }

    private static void RunPackPayload(Dictionary<string, string> options)
    {
        var sourceDirectory = Require(options, "source-dir");
        var outputZip = Require(options, "output-zip");
        PayloadUtilities.CreatePayloadZip(sourceDirectory, outputZip);
        Console.WriteLine(outputZip);
    }

    private static async Task<int> RunPublishS3Async(Dictionary<string, string> options)
    {
        var publisher = new PlondsPublisher();
        var result = await publisher.PublishAsync(new PlondsPublishOptions(
            ReleaseTag: Require(options, "release-tag"),
            Repository: Require(options, "repository"),
            ManifestPath: Require(options, "manifest"),
            ChangedZipPath: Require(options, "changed-zip"),
            WorkDir: Get(options, "work-dir", "plonds-publish-work") ?? "plonds-publish-work",
            S3KeyPrefix: Get(options, "s3-prefix", "lanmountain/update/plonds") ?? "lanmountain/update/plonds",
            S3: new PlondsS3ClientOptions(
                Endpoint: new Uri(Require(options, "s3-endpoint"), UriKind.Absolute),
                Region: Get(options, "s3-region", "us-east-1") ?? "us-east-1",
                Bucket: Require(options, "s3-bucket"),
                AccessKey: Require(options, "s3-access-key"),
                SecretKey: Require(options, "s3-secret-key"),
                PublicBaseUrl: Require(options, "s3-public-base-url"),
                PublicBaseKeyPrefix: Get(options, "s3-public-base-key-prefix", string.Empty) ?? string.Empty))).ConfigureAwait(false);

        Console.WriteLine($"Published PLONDS release {result.ReleaseTag}:");
        Console.WriteLine($"  Prefix:             {result.VersionPrefix}");
        Console.WriteLine($"  Manifest:           {result.ManifestUrl}");
        Console.WriteLine($"  ChangedZip:         {result.ChangedZipUrl}");
        Console.WriteLine($"  ChangedFolder:      {result.ChangedFolderUrl}");
        Console.WriteLine($"  ChangedFileCount:   {result.ChangedFileCount}");
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token[2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
            result[key] = value;
        }

        return result;
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string key)
    {
        if (options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"Missing required option --{key}");
    }

    private static string? Get(IReadOnlyDictionary<string, string> options, string key, string? defaultValue = null)
    {
        return options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PLONDS Tool");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build-delta              Build delta by comparing two payload zips");
        Console.WriteLine("    --platform <platform>   Platform identifier (e.g. windows-x64)");
        Console.WriteLine("    --current-version <v>  Current release version");
        Console.WriteLine("    --current-zip <file>   Current payload zip path");
        Console.WriteLine("    --output-dir <dir>     Output directory");
        Console.WriteLine("    [--channel <ch>]       Update channel (default: stable)");
        Console.WriteLine("    [--baseline-version <v>] Baseline version");
        Console.WriteLine("    [--baseline-zip <file>]  Baseline payload zip path");
        Console.WriteLine("    [--hash-algorithm <alg>]  sha256 or md5 (default: sha256)");
        Console.WriteLine("    [--launcher-path <path>] Launcher exe relative path");
        Console.WriteLine();
        Console.WriteLine("  build-delta-from-commits Build delta by analyzing git commits");
        Console.WriteLine("    --platform <platform>   Platform identifier");
        Console.WriteLine("    --current-version <v>  Current release version");
        Console.WriteLine("    --current-zip <file>   Current payload zip path");
        Console.WriteLine("    --output-dir <dir>     Output directory");
        Console.WriteLine("    --channel <ch>          Update channel");
        Console.WriteLine("    --baseline-tag <tag>    Baseline git tag");
        Console.WriteLine("    --current-tag <tag>    Current git tag");
        Console.WriteLine("    [--hash-algorithm <alg>]  sha256 or md5 (default: sha256)");
        Console.WriteLine("    [--source-dirs <dirs>]  Comma-separated source dirs to analyze");
        Console.WriteLine("    [--fallback-zip <file>] Baseline zip for fallback to file-compare");
        Console.WriteLine("    [--launcher-path <path>] Launcher exe relative path");
        Console.WriteLine();
        Console.WriteLine("  pack-payload             Pack a directory into a payload zip");
        Console.WriteLine("    --source-dir <dir>      Source directory");
        Console.WriteLine("    --output-zip <file>     Output zip path");
        Console.WriteLine();
        Console.WriteLine("  publish-s3               Publish PLONDS.json and changed.zip to S3");
        Console.WriteLine("    --release-tag <tag>     GitHub release tag");
        Console.WriteLine("    --repository <owner/repo> GitHub repository");
        Console.WriteLine("    --manifest <file>       PLONDS.json path");
        Console.WriteLine("    --changed-zip <file>    changed.zip path");
        Console.WriteLine("    --s3-endpoint <url>     S3-compatible endpoint");
        Console.WriteLine("    --s3-region <region>    S3 signing region");
        Console.WriteLine("    --s3-bucket <bucket>    S3 bucket");
        Console.WriteLine("    --s3-access-key <key>   S3 access key");
        Console.WriteLine("    --s3-secret-key <key>   S3 secret key");
        Console.WriteLine("    --s3-public-base-url <url> Public URL prefix for uploaded keys");
        Console.WriteLine("    [--s3-public-base-key-prefix <prefix>] Key prefix already represented by public URL");
        Console.WriteLine("    [--s3-prefix <prefix>]  Object key prefix (default: lanmountain/update/plonds)");
        Console.WriteLine("    [--work-dir <dir>]      Temporary publish work directory");
    }
}
