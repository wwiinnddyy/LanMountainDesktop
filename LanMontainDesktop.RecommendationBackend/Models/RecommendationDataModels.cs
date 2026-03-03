using System;
using System.Collections.Generic;

namespace LanMontainDesktop.RecommendationBackend.Models;

public sealed record DailyQuoteSnapshot(
    string Provider,
    string Content,
    string? Author,
    string? Source,
    DateTimeOffset FetchedAt);

public sealed record DailyPoetrySnapshot(
    string Provider,
    string Content,
    string? Origin,
    string? Author,
    string? Category,
    DateTimeOffset FetchedAt);

public sealed record DailyMovieRecommendation(
    string Provider,
    string Title,
    string? Rating,
    string? Description,
    string? Url,
    string? CoverUrl,
    DateTimeOffset FetchedAt);

public sealed record DailyArtworkSnapshot(
    string Provider,
    string Title,
    string? Artist,
    string? Year,
    string? Museum,
    string? ArtworkUrl,
    string? ImageUrl,
    DateTimeOffset FetchedAt);

public sealed record HotSearchEntry(
    string Provider,
    int Rank,
    string Title,
    string? HotValue,
    string? Summary,
    string? Url);

public sealed record RecommendationFeedSnapshot(
    DateTimeOffset FetchedAt,
    DailyQuoteSnapshot? DailyQuote,
    DailyPoetrySnapshot? DailyPoetry,
    DailyMovieRecommendation? DailyMovie,
    DailyArtworkSnapshot? DailyArtwork,
    IReadOnlyList<HotSearchEntry> HotSearches);
