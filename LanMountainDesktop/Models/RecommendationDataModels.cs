using System;

namespace LanMountainDesktop.Models;

public sealed record DailyArtworkSnapshot(
    string Provider,
    string Title,
    string? Artist,
    string? Year,
    string? Museum,
    string? ArtworkUrl,
    string? ImageUrl,
    DateTimeOffset FetchedAt);

public sealed record DailyPoetrySnapshot(
    string Provider,
    string Content,
    string? Origin,
    string? Author,
    string? Category,
    DateTimeOffset FetchedAt);
