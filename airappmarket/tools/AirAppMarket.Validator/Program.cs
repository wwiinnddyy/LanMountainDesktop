using System.Text.Json;

return await RunAsync(args);

static Task<int> RunAsync(string[] args)
{
    try
    {
        var indexPath = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "index.json"));
        var schemaPath = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(indexPath)!, "schema", "airappmarket-index.schema.json"));

        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException($"Market index '{indexPath}' was not found.", indexPath);
        }

        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException($"Market schema '{schemaPath}' was not found.", schemaPath);
        }

        JsonDocument.Parse(File.ReadAllText(schemaPath));
        var document = MarketIndex.Load(File.ReadAllText(indexPath), indexPath);

        Console.WriteLine($"Validated '{indexPath}'.");
        Console.WriteLine($"Source: {document.SourceName} ({document.SourceId})");
        Console.WriteLine($"Plugins: {document.Plugins.Count}");
        return Task.FromResult(0);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return Task.FromResult(1);
    }
}

internal sealed class MarketIndex
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string SchemaVersion { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public List<MarketPlugin> Plugins { get; init; } = [];

    public static MarketIndex Load(string json, string sourceName)
    {
        var document = JsonSerializer.Deserialize<MarketIndex>(
            json.TrimStart('\uFEFF'),
            SerializerOptions) ?? throw new InvalidOperationException($"Failed to parse market index '{sourceName}'.");

        return document.ValidateAndNormalize(sourceName);
    }

    private MarketIndex ValidateAndNormalize(string sourceName)
    {
        var normalizedPlugins = new List<MarketPlugin>(Plugins.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in Plugins)
        {
            var normalizedPlugin = plugin.ValidateAndNormalize(sourceName);
            if (!seenIds.Add(normalizedPlugin.Id))
            {
                throw new InvalidOperationException(
                    $"Market index '{sourceName}' contains duplicate plugin id '{normalizedPlugin.Id}'.");
            }

            normalizedPlugins.Add(normalizedPlugin);
        }

        return new MarketIndex
        {
            SchemaVersion = RequireValue(SchemaVersion, nameof(SchemaVersion), sourceName),
            SourceId = RequireValue(SourceId, nameof(SourceId), sourceName),
            SourceName = RequireValue(SourceName, nameof(SourceName), sourceName),
            GeneratedAt = GeneratedAt == default
                ? throw new InvalidOperationException($"Market index '{sourceName}' is missing a valid generatedAt timestamp.")
                : GeneratedAt,
            Plugins = normalizedPlugins
        };
    }

    internal static string RequireValue(string? value, string propertyName, string sourceName)
    {
        var normalized = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"Market index '{sourceName}' is missing required property '{propertyName}'.");
        }

        return normalized;
    }

    internal static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static string NormalizeVersion(string? value, string propertyName, string sourceName)
    {
        var normalized = RequireValue(value, propertyName, sourceName);
        if (!TryParseVersion(normalized, out _))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid version '{normalized}' for '{propertyName}'.");
        }

        return normalized;
    }

    internal static bool TryParseVersion(string? value, out Version? version)
    {
        version = null;
        var normalized = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var separatorIndex = normalized.IndexOfAny(['-', '+', ' ']);
        if (separatorIndex > 0)
        {
            normalized = normalized[..separatorIndex];
        }

        if (!Version.TryParse(normalized, out var parsed))
        {
            return false;
        }

        version = new Version(
            Math.Max(0, parsed.Major),
            Math.Max(0, parsed.Minor),
            Math.Max(0, parsed.Build));
        return true;
    }

    internal static void EnsureUrl(string? value, string propertyName, string sourceName)
    {
        var normalized = RequireValue(value, propertyName, sourceName);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid URL '{normalized}' for '{propertyName}'.");
        }
    }
}

internal sealed class MarketPlugin
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ApiVersion { get; init; } = string.Empty;
    public string MinHostVersion { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long PackageSizeBytes { get; init; }
    public string IconUrl { get; init; } = string.Empty;
    public string HomepageUrl { get; init; } = string.Empty;
    public string RepositoryUrl { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string ReleaseNotes { get; init; } = string.Empty;

    public MarketPlugin ValidateAndNormalize(string sourceName)
    {
        var tagSource = Tags ?? [];
        var normalizedTags = tagSource
            .Select(MarketIndex.NormalizeValue)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedTags.Count != tagSource.Count(tag => !string.IsNullOrWhiteSpace(tag)))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' contains duplicate or blank tags for plugin '{Id}'.");
        }

        var normalizedSha = MarketIndex.RequireValue(Sha256, nameof(Sha256), sourceName).ToLowerInvariant();
        if (normalizedSha.Length != 64 || normalizedSha.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid SHA-256 '{normalizedSha}' for plugin '{Id}'.");
        }

        MarketIndex.EnsureUrl(DownloadUrl, nameof(DownloadUrl), sourceName);
        MarketIndex.EnsureUrl(IconUrl, nameof(IconUrl), sourceName);
        MarketIndex.EnsureUrl(HomepageUrl, nameof(HomepageUrl), sourceName);
        MarketIndex.EnsureUrl(RepositoryUrl, nameof(RepositoryUrl), sourceName);

        if (PackageSizeBytes <= 0)
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid packageSizeBytes '{PackageSizeBytes}' for plugin '{Id}'.");
        }

        if (PublishedAt == default || UpdatedAt == default)
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing valid publish timestamps for plugin '{Id}'.");
        }

        return new MarketPlugin
        {
            Id = MarketIndex.RequireValue(Id, nameof(Id), sourceName),
            Name = MarketIndex.RequireValue(Name, nameof(Name), sourceName),
            Description = MarketIndex.RequireValue(Description, nameof(Description), sourceName),
            Author = MarketIndex.RequireValue(Author, nameof(Author), sourceName),
            Version = MarketIndex.NormalizeVersion(Version, nameof(Version), sourceName),
            ApiVersion = MarketIndex.NormalizeVersion(ApiVersion, nameof(ApiVersion), sourceName),
            MinHostVersion = MarketIndex.NormalizeVersion(MinHostVersion, nameof(MinHostVersion), sourceName),
            DownloadUrl = MarketIndex.RequireValue(DownloadUrl, nameof(DownloadUrl), sourceName),
            Sha256 = normalizedSha,
            PackageSizeBytes = PackageSizeBytes,
            IconUrl = MarketIndex.RequireValue(IconUrl, nameof(IconUrl), sourceName),
            HomepageUrl = MarketIndex.RequireValue(HomepageUrl, nameof(HomepageUrl), sourceName),
            RepositoryUrl = MarketIndex.RequireValue(RepositoryUrl, nameof(RepositoryUrl), sourceName),
            Tags = normalizedTags,
            PublishedAt = PublishedAt,
            UpdatedAt = UpdatedAt,
            ReleaseNotes = MarketIndex.RequireValue(ReleaseNotes, nameof(ReleaseNotes), sourceName)
        };
    }
}
