using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public sealed record DailyArtworkSnapshot(
    string Provider,
    string Title,
    string? Artist,
    string? Year,
    string? Museum,
    string? ArtworkUrl,
    string? ImageUrl,
    string? ThumbnailDataUrl,
    DateTimeOffset FetchedAt);

public sealed record DailyPoetrySnapshot(
    string Provider,
    string Content,
    string? Origin,
    string? Author,
    string? Category,
    DateTimeOffset FetchedAt);

public sealed record DailyNewsItemSnapshot(
    string Title,
    string? Summary,
    string Url,
    string? ImageUrl,
    string? PublishTime);

public sealed record DailyNewsSnapshot(
    string Provider,
    string Source,
    IReadOnlyList<DailyNewsItemSnapshot> Items,
    DateTimeOffset FetchedAt);

public sealed record BilibiliHotSearchItemSnapshot(
    string Title,
    string Keyword,
    string Url,
    long? HeatScore,
    bool HasHotTag,
    string? IconUrl);

public sealed record BilibiliHotSearchSnapshot(
    string Provider,
    string Source,
    string SearchPlaceholder,
    string SearchUrl,
    string MoreHotUrl,
    IReadOnlyList<BilibiliHotSearchItemSnapshot> Items,
    DateTimeOffset FetchedAt);

public sealed record DailyWordSnapshot(
    string Provider,
    string Word,
    string? UkPronunciation,
    string? UsPronunciation,
    string Meaning,
    string? ExampleSentence,
    string? ExampleTranslation,
    string? SourceUrl,
    DateTimeOffset FetchedAt);

public sealed record ExchangeRateSnapshot(
    string Provider,
    string Source,
    string BaseCurrency,
    string TargetCurrency,
    decimal Rate,
    DateTimeOffset FetchedAt);

public sealed record Stcn24ForumPostItemSnapshot(
    string Title,
    string Url,
    string? AuthorDisplayName,
    string? AuthorAvatarUrl,
    DateTimeOffset? CreatedAt);

public sealed record Stcn24ForumPostsSnapshot(
    string Provider,
    string Source,
    IReadOnlyList<Stcn24ForumPostItemSnapshot> Items,
    DateTimeOffset FetchedAt);
