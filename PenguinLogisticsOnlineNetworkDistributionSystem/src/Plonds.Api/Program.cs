using Microsoft.Extensions.Options;
using Plonds.Api.Configuration;
using Plonds.Api.Services;
using Plonds.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PlondsApiOptions>(builder.Configuration.GetSection("Plonds"));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<PlondsApiOptions>>().Value;
    return options;
});
builder.Services.AddSingleton<IPlondsManifestStore>(sp =>
{
    var options = sp.GetRequiredService<PlondsApiOptions>();
    return new FileSystemPlondsManifestStore(options);
});

var app = builder.Build();

var apiBasePath = app.Configuration["Plonds:ApiBasePath"];
if (string.IsNullOrWhiteSpace(apiBasePath))
{
    apiBasePath = PlondsConstants.DefaultApiBasePath;
}

if (!apiBasePath.StartsWith('/'))
{
    apiBasePath = "/" + apiBasePath;
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", protocol = PlondsConstants.ProtocolName, version = PlondsConstants.ProtocolVersion }));

app.MapGet($"{apiBasePath}/metadata", async (IPlondsManifestStore store, CancellationToken cancellationToken) =>
{
    var catalog = await store.GetCatalogAsync(cancellationToken);
    return Results.Ok(catalog);
});

app.MapGet($"{apiBasePath}/channels/{{channel}}/{{platform}}/latest", async (
    string channel,
    string platform,
    string? currentVersion,
    IPlondsManifestStore store,
    CancellationToken cancellationToken) =>
{
    var latest = await store.GetLatestAsync(channel, platform, cancellationToken);
    if (latest is null)
    {
        return Results.NotFound(new
        {
            error = "latest_pointer_not_found",
            channel,
            platform
        });
    }

    if (!string.IsNullOrWhiteSpace(currentVersion) &&
        Version.TryParse(currentVersion, out var current) &&
        Version.TryParse(latest.Version, out var target) &&
        target <= current)
    {
        return Results.NoContent();
    }

    return Results.Ok(latest);
});

app.MapGet($"{apiBasePath}/distributions/{{distributionId}}", async (string distributionId, IPlondsManifestStore store, CancellationToken cancellationToken) =>
{
    var distribution = await store.GetDistributionAsync(distributionId, cancellationToken);
    if (distribution is null)
    {
        return Results.NotFound(new
        {
            error = "distribution_not_found",
            distributionId
        });
    }

    return Results.Ok(distribution);
});

app.Run();

