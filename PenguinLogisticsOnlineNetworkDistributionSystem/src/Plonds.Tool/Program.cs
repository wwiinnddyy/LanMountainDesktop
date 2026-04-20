using Plonds.Core.Publishing;
using Plonds.Core.Security;

return await PlondsCli.RunAsync(args);

internal static class PlondsCli
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return Task.FromResult(1);
        }

        var command = args[0].Trim().ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());

        try
        {
            switch (command)
            {
                case "generate":
                    RunGenerate(options);
                    return Task.FromResult(0);
                case "sign":
                    RunSign(options);
                    return Task.FromResult(0);
                case "publish":
                    RunPublish(options);
                    return Task.FromResult(0);
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return Task.FromResult(1);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return Task.FromResult(1);
        }
    }

    private static void RunGenerate(Dictionary<string, string> options)
    {
        var generator = new PlondsGenerator();
        var result = generator.Generate(new PlondsGenerateOptions(
            CurrentVersion: Require(options, "current-version"),
            CurrentDirectory: Require(options, "current-dir"),
            Platform: Require(options, "platform"),
            OutputRoot: Require(options, "output-dir"),
            PreviousVersion: Get(options, "previous-version", "0.0.0") ?? "0.0.0",
            PreviousDirectory: Get(options, "previous-dir"),
            Channel: Get(options, "channel", "stable") ?? "stable",
            DistributionId: Get(options, "distribution-id"),
            RepoBaseUrl: Get(options, "repo-base-url"),
            FileMapUrl: Get(options, "file-map-url"),
            FileMapSignatureUrl: Get(options, "file-map-signature-url"),
            InstallerDirectory: Get(options, "installer-directory"),
            InstallerBaseUrl: Get(options, "installer-base-url")));

        Console.WriteLine($"Generated PLONDS artifacts for {result.Platform}: {result.DistributionId}");
        Console.WriteLine(result.FileMapPath);
    }

    private static void RunSign(Dictionary<string, string> options)
    {
        var signer = new RsaFileSigner();
        var signaturePath = signer.SignFile(
            Require(options, "manifest"),
            Require(options, "private-key"),
            Get(options, "output"));
        Console.WriteLine(signaturePath);
    }

    private static void RunPublish(Dictionary<string, string> options)
    {
        var publisher = new PlondsPublisher();
        var results = publisher.Publish(new PlondsPublishOptions(
            Version: Require(options, "version"),
            AppArtifactsRoot: Require(options, "app-artifacts-root"),
            InstallerArtifactsRoot: Require(options, "installer-artifacts-root"),
            OutputRoot: Require(options, "output-dir"),
            PrivateKeyPath: Require(options, "private-key"),
            Channel: Get(options, "channel", "stable") ?? "stable",
            BaselineRoot: Get(options, "baseline-root"),
            RepoBaseUrl: Get(options, "repo-base-url"),
            InstallerBaseUrl: Get(options, "installer-base-url")));

        foreach (var result in results)
        {
            Console.WriteLine($"{result.Platform}: {result.DistributionId}");
        }
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
        Console.WriteLine("  generate --current-version <v> --current-dir <dir> --platform <platform> --output-dir <dir> [--previous-version <v>] [--previous-dir <dir>]");
        Console.WriteLine("  sign --manifest <file> --private-key <pem> [--output <file>]");
        Console.WriteLine("  publish --version <v> --app-artifacts-root <dir> --installer-artifacts-root <dir> --output-dir <dir> --private-key <pem> [--baseline-root <dir>]");
    }
}
