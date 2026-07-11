using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace LanMountainDesktop.Services.RssReader;

public sealed record RssSource(
    string Id,
    string Title,
    string FeedUrl,
    string? SiteUrl,
    string? Folder,
    bool IsEnabled,
    int? RefreshIntervalMinutes,
    string? ETag,
    DateTimeOffset? LastModified,
    DateTimeOffset? LastRefresh,
    string? LastError);

public sealed record RssEntry(
    string Id,
    string SourceId,
    string SourceTitle,
    string Title,
    string? Link,
    string? Author,
    string Summary,
    string Content,
    DateTimeOffset PublishedAt,
    bool IsRead,
    bool IsFavorite);

public sealed record RssReaderSettings(int RefreshIntervalMinutes, int RetentionDays, bool LoadRemoteImages)
{
    public static RssReaderSettings Default { get; } = new(30, 30, false);
}

public sealed record RssFeedProbe(string Title, string FeedUrl, string? SiteUrl, string Format);

public sealed record RssOpmlImportResult(int Added, int Skipped, int Failed);

public sealed class RssReaderService : IDisposable
{
    private const int MaxResponseBytes = 5 * 1024 * 1024;
    private const int MaxEntriesPerSource = 500;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly string _databasePath;
    private readonly Timer _revisionTimer;
    private long _lastRevision;
    private bool _disposed;

    public RssReaderService(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            AppDataPathProvider.GetDataRoot(), "AirApps", "RssReader", "rss.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        InitializeDatabase();
        _lastRevision = GetRevision();
        _revisionTimer = new Timer(CheckRevision, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public event EventHandler? Changed;

    public string DatabasePath => _databasePath;

    public IReadOnlyList<RssSource> GetSources()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, feed_url, site_url, folder, enabled, refresh_interval,
                   etag, last_modified, last_refresh, last_error
            FROM rss_sources ORDER BY COALESCE(folder, ''), title COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<RssSource>();
        while (reader.Read())
        {
            result.Add(ReadSource(reader));
        }

        return result;
    }

    public IReadOnlyList<RssEntry> GetEntries(
        string? sourceId = null,
        bool unreadOnly = false,
        bool favoritesOnly = false,
        int limit = 100)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            filters.Add("e.source_id = $sourceId");
            command.Parameters.AddWithValue("$sourceId", sourceId);
        }
        if (unreadOnly) filters.Add("e.is_read = 0");
        if (favoritesOnly) filters.Add("e.is_favorite = 1");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        command.CommandText = $"""
            SELECT e.id, e.source_id, s.title, e.title, e.link, e.author,
                   e.summary, e.content, e.published_at, e.is_read, e.is_favorite
            FROM rss_entries e JOIN rss_sources s ON s.id = e.source_id
            {(filters.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", filters))}
            ORDER BY e.published_at DESC LIMIT $limit;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<RssEntry>();
        while (reader.Read()) result.Add(ReadEntry(reader));
        return result;
    }

    public RssEntry? GetEntry(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.id, e.source_id, s.title, e.title, e.link, e.author,
                   e.summary, e.content, e.published_at, e.is_read, e.is_favorite
            FROM rss_entries e JOIN rss_sources s ON s.id = e.source_id WHERE e.id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public async Task<RssFeedProbe> ProbeAsync(string feedUrl, CancellationToken cancellationToken = default)
    {
        var normalizedUrl = NormalizeFeedUrl(feedUrl);
        var document = await DownloadFeedAsync(normalizedUrl, null, null, cancellationToken).ConfigureAwait(false);
        if (document.NotModified || document.Bytes is null) throw new InvalidDataException("Feed returned no content.");
        var parsed = ParseFeed(document.Bytes, normalizedUrl);
        return new RssFeedProbe(parsed.Title, normalizedUrl, parsed.SiteUrl, parsed.Format);
    }

    public async Task<RssSource> AddSourceAsync(
        string feedUrl,
        string? title = null,
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedUrl = NormalizeFeedUrl(feedUrl);
        var document = await DownloadFeedAsync(normalizedUrl, null, null, cancellationToken).ConfigureAwait(false);
        if (document.Bytes is null) throw new InvalidDataException("Feed returned no content.");
        var parsed = ParseFeed(document.Bytes, normalizedUrl);
        var sourceId = CreateStableId(normalizedUrl);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO rss_sources(id, title, feed_url, site_url, folder, enabled, etag, last_modified, last_refresh)
                VALUES($id, $title, $url, $site, $folder, 1, $etag, $modified, $refresh)
                ON CONFLICT(feed_url) DO UPDATE SET
                    title = excluded.title, site_url = excluded.site_url,
                    folder = COALESCE(excluded.folder, rss_sources.folder), enabled = 1;
                """;
            command.Parameters.AddWithValue("$id", sourceId);
            command.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(title) ? parsed.Title : title.Trim());
            command.Parameters.AddWithValue("$url", normalizedUrl);
            command.Parameters.AddWithValue("$site", (object?)parsed.SiteUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$folder", NormalizeOptional(folder));
            command.Parameters.AddWithValue("$etag", (object?)document.ETag ?? DBNull.Value);
            command.Parameters.AddWithValue("$modified", ToDb(document.LastModified));
            command.Parameters.AddWithValue("$refresh", ToDb(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }
        UpsertEntries(connection, transaction, sourceId, normalizedUrl, parsed.Items);
        IncrementRevision(connection, transaction);
        transaction.Commit();
        RaiseChanged();
        return GetSources().Single(source => source.FeedUrl == normalizedUrl);
    }

    public void UpdateSource(string id, string title, string? folder, bool enabled, int? refreshIntervalMinutes)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE rss_sources SET title=$title, folder=$folder, enabled=$enabled,
                refresh_interval=$interval WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(title) ? "Untitled Feed" : title.Trim());
        command.Parameters.AddWithValue("$folder", NormalizeOptional(folder));
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$interval", refreshIntervalMinutes is null ? DBNull.Value : Math.Clamp(refreshIntervalMinutes.Value, 15, 1440));
        command.ExecuteNonQuery();
        IncrementRevision(connection, transaction);
        transaction.Commit();
        RaiseChanged();
    }

    public void DeleteSource(string id, bool preserveFavorites)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (preserveFavorites)
        {
            using var preserve = connection.CreateCommand();
            preserve.Transaction = transaction;
            preserve.CommandText = "DELETE FROM rss_entries WHERE source_id=$id AND is_favorite=0; UPDATE rss_sources SET enabled=0 WHERE id=$id;";
            preserve.Parameters.AddWithValue("$id", id);
            preserve.ExecuteNonQuery();
        }
        else
        {
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM rss_sources WHERE id=$id;";
            delete.Parameters.AddWithValue("$id", id);
            delete.ExecuteNonQuery();
        }
        IncrementRevision(connection, transaction);
        transaction.Commit();
        RaiseChanged();
    }

    public void MarkRead(string entryId, bool isRead = true) => UpdateEntryFlag(entryId, "is_read", isRead);
    public void SetFavorite(string entryId, bool isFavorite) => UpdateEntryFlag(entryId, "is_favorite", isFavorite);

    public void MarkAllRead(string? sourceId = null)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sourceId is null
            ? "UPDATE rss_entries SET is_read=1 WHERE is_read=0;"
            : "UPDATE rss_entries SET is_read=1 WHERE is_read=0 AND source_id=$sourceId;";
        if (sourceId is not null) command.Parameters.AddWithValue("$sourceId", sourceId);
        command.ExecuteNonQuery();
        IncrementRevision(connection, transaction);
        transaction.Commit();
        RaiseChanged();
    }

    public RssReaderSettings GetSettings()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM rss_metadata WHERE key='settings';";
        var value = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(value)) return RssReaderSettings.Default;
        var parts = value.Split('|');
        return new RssReaderSettings(
            parts.Length > 0 && int.TryParse(parts[0], out var refresh) ? refresh : 30,
            parts.Length > 1 && int.TryParse(parts[1], out var retention) ? retention : 30,
            parts.Length > 2 && bool.TryParse(parts[2], out var images) && images);
    }

    public void SaveSettings(RssReaderSettings settings)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        SetMetadata(connection, transaction, "settings", $"{NormalizeRefresh(settings.RefreshIntervalMinutes)}|{Math.Clamp(settings.RetentionDays, 1, 365)}|{settings.LoadRemoteImages}");
        IncrementRevision(connection, transaction);
        transaction.Commit();
        RaiseChanged();
    }

    public string? GetPendingEntryId()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM rss_metadata WHERE key='pending_entry';";
        return command.ExecuteScalar() as string;
    }

    public void SetPendingEntryId(string? entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId)) return;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        SetMetadata(connection, transaction, "pending_entry", entryId.Trim());
        IncrementRevision(connection, transaction);
        transaction.Commit();
        RaiseChanged();
    }

    public async Task RefreshAllAsync(bool force = true, CancellationToken cancellationToken = default)
    {
        var globalInterval = GetSettings().RefreshIntervalMinutes;
        var now = DateTimeOffset.UtcNow;
        var sources = GetSources().Where(source =>
        {
            if (!source.IsEnabled) return false;
            if (force) return true;
            var interval = source.RefreshIntervalMinutes ?? globalInterval;
            return interval > 0 && (source.LastRefresh is null || now - source.LastRefresh >= TimeSpan.FromMinutes(interval));
        }).ToArray();
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(sources.Select(async source =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await RefreshSourceAsync(source.Id, cancellationToken).ConfigureAwait(false); }
            finally { gate.Release(); }
        })).ConfigureAwait(false);
        Cleanup();
    }

    public async Task RefreshSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var source = GetSources().FirstOrDefault(item => item.Id == sourceId)
            ?? throw new KeyNotFoundException("RSS source was not found.");
        try
        {
            var document = await DownloadFeedAsync(source.FeedUrl, source.ETag, source.LastModified, cancellationToken).ConfigureAwait(false);
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (!document.NotModified && document.Bytes is not null)
            {
                var parsed = ParseFeed(document.Bytes, source.FeedUrl);
                UpsertEntries(connection, transaction, source.Id, source.FeedUrl, parsed.Items);
            }
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE rss_sources SET etag=$etag,last_modified=$modified,last_refresh=$refresh,last_error=NULL WHERE id=$id;";
            update.Parameters.AddWithValue("$id", source.Id);
            update.Parameters.AddWithValue("$etag", (object?)document.ETag ?? (object?)source.ETag ?? DBNull.Value);
            update.Parameters.AddWithValue("$modified", ToDb(document.LastModified ?? source.LastModified));
            update.Parameters.AddWithValue("$refresh", ToDb(DateTimeOffset.UtcNow));
            update.ExecuteNonQuery();
            IncrementRevision(connection, transaction);
            transaction.Commit();
            RaiseChanged();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_sources SET last_error=$error,last_refresh=$refresh WHERE id=$id;";
            command.Parameters.AddWithValue("$id", source.Id);
            command.Parameters.AddWithValue("$error", ex.Message);
            command.Parameters.AddWithValue("$refresh", ToDb(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }
    }

    public async Task<RssOpmlImportResult> ImportOpmlAsync(string path, CancellationToken cancellationToken = default)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        var outlines = document.Descendants("outline")
            .Where(element => element.Attribute("xmlUrl") is not null)
            .Select(element => new
            {
                Url = element.Attribute("xmlUrl")!.Value,
                Title = element.Attribute("title")?.Value ?? element.Attribute("text")?.Value,
                Folder = string.Join(" / ", element.Ancestors("outline").Reverse()
                    .Select(ancestor => ancestor.Attribute("text")?.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
            }).ToArray();
        var existing = GetSources().Select(source => source.FeedUrl).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0; var skipped = 0; var failed = 0;
        foreach (var outline in outlines)
        {
            try
            {
                var normalized = NormalizeFeedUrl(outline.Url);
                if (!existing.Add(normalized)) { skipped++; continue; }
                await AddSourceAsync(normalized, outline.Title, outline.Folder, cancellationToken).ConfigureAwait(false);
                added++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { failed++; }
        }
        return new RssOpmlImportResult(added, skipped, failed);
    }

    public void ExportOpml(string path)
    {
        var body = new XElement("body");
        foreach (var group in GetSources().GroupBy(source => source.Folder ?? string.Empty))
        {
            var parent = string.IsNullOrWhiteSpace(group.Key) ? body : new XElement("outline", new XAttribute("text", group.Key));
            foreach (var source in group)
            {
                parent.Add(new XElement("outline",
                    new XAttribute("type", "rss"), new XAttribute("text", source.Title),
                    new XAttribute("title", source.Title), new XAttribute("xmlUrl", source.FeedUrl),
                    source.SiteUrl is null ? null : new XAttribute("htmlUrl", source.SiteUrl)));
            }
            if (parent != body) body.Add(parent);
        }
        new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("opml", new XAttribute("version", "2.0"),
                new XElement("head", new XElement("title", "LanMountainDesktop RSS subscriptions")), body)).Save(path);
    }

    public static string NormalizeFeedUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("RSS address must be an absolute HTTP or HTTPS URL.", nameof(value));
        var builder = new UriBuilder(uri) { Fragment = string.Empty, Host = uri.Host.ToLowerInvariant() };
        if ((builder.Scheme == "https" && builder.Port == 443) || (builder.Scheme == "http" && builder.Port == 80)) builder.Port = -1;
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    internal static RssFeedProbe ParseProbe(byte[] bytes, string feedUrl)
    {
        var parsed = ParseFeed(bytes, NormalizeFeedUrl(feedUrl));
        return new RssFeedProbe(parsed.Title, NormalizeFeedUrl(feedUrl), parsed.SiteUrl, parsed.Format);
    }

    public static string SanitizeHtml(string? html, bool loadRemoteImages)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var value = Regex.Replace(html, @"<(script|style|iframe|object|embed|form)[^>]*>.*?</\1\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, """\s+on[a-z]+\s*=\s*(['"]).*?\1""", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, """\s+(src|href)\s*=\s*(['"])\s*javascript:.*?\2""", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!loadRemoteImages) value = Regex.Replace(value, @"<img\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        return value;
    }

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _revisionTimer.Dispose();
    }

    private void InitializeDatabase()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS rss_sources(
              id TEXT PRIMARY KEY, title TEXT NOT NULL, feed_url TEXT NOT NULL UNIQUE,
              site_url TEXT, folder TEXT, enabled INTEGER NOT NULL DEFAULT 1,
              refresh_interval INTEGER, etag TEXT, last_modified TEXT,
              last_refresh TEXT, last_error TEXT);
            CREATE TABLE IF NOT EXISTS rss_entries(
              id TEXT PRIMARY KEY, source_id TEXT NOT NULL REFERENCES rss_sources(id) ON DELETE CASCADE,
              title TEXT NOT NULL, link TEXT, author TEXT, summary TEXT NOT NULL DEFAULT '',
              content TEXT NOT NULL DEFAULT '', published_at TEXT NOT NULL,
              is_read INTEGER NOT NULL DEFAULT 0, is_favorite INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS ix_rss_entries_source_date ON rss_entries(source_id, published_at DESC);
            CREATE INDEX IF NOT EXISTS ix_rss_entries_date ON rss_entries(published_at DESC);
            CREATE TABLE IF NOT EXISTS rss_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT OR IGNORE INTO rss_metadata(key,value) VALUES('revision','0');
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        { Timeout = RequestTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-RssReader/1.0");
        return client;
    }

    private static async Task<DownloadResult> DownloadFeedAsync(string url, string? etag, DateTimeOffset? modified, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(etag) && EntityTagHeaderValue.TryParse(etag, out var tag)) request.Headers.IfNoneMatch.Add(tag);
        if (modified is not null) request.Headers.IfModifiedSince = modified;
        using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified) return new DownloadResult(null, etag, modified, true);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidDataException("RSS response exceeds 5 MB.");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > MaxResponseBytes) throw new InvalidDataException("RSS response exceeds 5 MB.");
            memory.Write(buffer, 0, read);
        }
        return new DownloadResult(memory.ToArray(), response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, false);
    }

    private static ParsedFeed ParseFeed(byte[] bytes, string feedUrl)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxResponseBytes };
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, settings);
        var feed = SyndicationFeed.Load(reader) ?? throw new InvalidDataException("Unsupported RSS or Atom document.");
        var title = string.IsNullOrWhiteSpace(feed.Title?.Text) ? new Uri(feedUrl).Host : feed.Title.Text.Trim();
        var siteUrl = ResolveLink(feed.Links.FirstOrDefault(link => link.RelationshipType is null or "alternate")?.Uri, feedUrl);
        var items = feed.Items.Select(item =>
        {
            var link = ResolveLink(item.Links.FirstOrDefault(link => link.RelationshipType is null or "alternate")?.Uri, feedUrl);
            var published = item.PublishDate != DateTimeOffset.MinValue ? item.PublishDate :
                item.LastUpdatedTime != DateTimeOffset.MinValue ? item.LastUpdatedTime : DateTimeOffset.UtcNow;
            var summary = item.Summary?.Text ?? string.Empty;
            var content = item.Content is TextSyndicationContent textContent ? textContent.Text : summary;
            var stable = !string.IsNullOrWhiteSpace(item.Id) ? item.Id : link ?? $"{item.Title?.Text}|{published:O}";
            return new ParsedItem(CreateStableId(feedUrl + "|" + stable), item.Title?.Text?.Trim() ?? "Untitled", link,
                item.Authors.FirstOrDefault()?.Name, summary, content, published);
        }).ToArray();
        var format = Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 512))).Contains("<feed", StringComparison.OrdinalIgnoreCase) ? "Atom" : "RSS";
        return new ParsedFeed(title, siteUrl, format, items);
    }

    private static void UpsertEntries(SqliteConnection connection, SqliteTransaction transaction, string sourceId, string feedUrl, IEnumerable<ParsedItem> items)
    {
        foreach (var item in items)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO rss_entries(id,source_id,title,link,author,summary,content,published_at)
                VALUES($id,$source,$title,$link,$author,$summary,$content,$published)
                ON CONFLICT(id) DO UPDATE SET title=excluded.title,link=excluded.link,author=excluded.author,
                    summary=excluded.summary,content=excluded.content,published_at=excluded.published_at;
                """;
            command.Parameters.AddWithValue("$id", item.Id);
            command.Parameters.AddWithValue("$source", sourceId);
            command.Parameters.AddWithValue("$title", item.Title);
            command.Parameters.AddWithValue("$link", (object?)item.Link ?? DBNull.Value);
            command.Parameters.AddWithValue("$author", (object?)item.Author ?? DBNull.Value);
            command.Parameters.AddWithValue("$summary", item.Summary);
            command.Parameters.AddWithValue("$content", item.Content);
            command.Parameters.AddWithValue("$published", ToDb(item.PublishedAt));
            command.ExecuteNonQuery();
        }
    }

    private static string? ResolveLink(Uri? link, string feedUrl)
    {
        if (link is null) return null;
        if (link.IsAbsoluteUri) return link.AbsoluteUri;
        return Uri.TryCreate(new Uri(feedUrl), link, out var absolute) ? absolute.AbsoluteUri : null;
    }

    private void Cleanup()
    {
        var retention = GetSettings().RetentionDays;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM rss_entries WHERE is_favorite=0 AND published_at < $cutoff;
            DELETE FROM rss_entries WHERE is_favorite=0 AND id IN (
              SELECT id FROM (SELECT id, ROW_NUMBER() OVER(PARTITION BY source_id ORDER BY published_at DESC) row_number FROM rss_entries)
              WHERE row_number > $limit);
            """;
        command.Parameters.AddWithValue("$cutoff", ToDb(DateTimeOffset.UtcNow.AddDays(-retention)));
        command.Parameters.AddWithValue("$limit", MaxEntriesPerSource);
        command.ExecuteNonQuery();
        IncrementRevision(connection, transaction);
        transaction.Commit();
    }

    private void UpdateEntryFlag(string entryId, string column, bool value)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE rss_entries SET {column}=$value WHERE id=$id;";
        command.Parameters.AddWithValue("$id", entryId);
        command.Parameters.AddWithValue("$value", value ? 1 : 0);
        command.ExecuteNonQuery();
        IncrementRevision(connection, transaction);
        transaction.Commit();
        RaiseChanged();
    }

    private long GetRevision()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM rss_metadata WHERE key='revision';";
        return long.TryParse(command.ExecuteScalar()?.ToString(), out var revision) ? revision : 0;
    }

    private void CheckRevision(object? state)
    {
        if (_disposed) return;
        try
        {
            var revision = GetRevision();
            if (revision == Interlocked.Read(ref _lastRevision)) return;
            Interlocked.Exchange(ref _lastRevision, revision);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    private void RaiseChanged()
    {
        _lastRevision = GetRevision();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void IncrementRevision(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE rss_metadata SET value=CAST(value AS INTEGER)+1 WHERE key='revision';";
        command.ExecuteNonQuery();
    }

    private static void SetMetadata(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO rss_metadata(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static RssSource ReadSource(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5) != 0,
        reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7),
        ParseDate(reader, 8), ParseDate(reader, 9), reader.IsDBNull(10) ? null : reader.GetString(10));

    private static RssEntry ReadEntry(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetString(6), reader.GetString(7), DateTimeOffset.Parse(reader.GetString(8)), reader.GetInt64(9) != 0, reader.GetInt64(10) != 0);

    private static DateTimeOffset? ParseDate(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : DateTimeOffset.Parse(reader.GetString(index));
    private static object NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static object ToDb(DateTimeOffset? value) => value is null ? DBNull.Value : value.Value.ToUniversalTime().ToString("O");
    private static int NormalizeRefresh(int value) => value is 0 or 15 or 30 or 60 ? value : 30;
    private static string CreateStableId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record DownloadResult(byte[]? Bytes, string? ETag, DateTimeOffset? LastModified, bool NotModified);
    private sealed record ParsedFeed(string Title, string? SiteUrl, string Format, IReadOnlyList<ParsedItem> Items);
    private sealed record ParsedItem(string Id, string Title, string? Link, string? Author, string Summary, string Content, DateTimeOffset PublishedAt);
}
