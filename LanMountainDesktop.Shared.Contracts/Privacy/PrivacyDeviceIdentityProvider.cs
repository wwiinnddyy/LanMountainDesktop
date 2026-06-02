using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.Shared.Contracts.Privacy;

public sealed partial class PrivacyDeviceIdentityProvider : IPrivacyDeviceIdentityProvider
{
    public const string DefaultIdentityFileName = "privacy-device.identity.json";

    private readonly string _identityPath;
    private readonly object _gate = new();

    public PrivacyDeviceIdentityProvider(string? identityPath = null)
    {
        _identityPath = string.IsNullOrWhiteSpace(identityPath)
            ? GetDefaultIdentityPath()
            : Path.GetFullPath(identityPath);
    }

    public string GetOrCreateDeviceId()
    {
        lock (_gate)
        {
            var existing = TryLoad();
            if (!string.IsNullOrWhiteSpace(existing?.DeviceId))
            {
                return existing.DeviceId;
            }

            var created = new PrivacyDeviceIdentityDocument(
                SchemaVersion: 1,
                DeviceId: GenerateDeviceId(),
                CreatedAtUtc: DateTimeOffset.UtcNow);
            Save(created);
            return created.DeviceId;
        }
    }

    public static string GetDefaultIdentityPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        return Path.Combine(root, "LanMountainDesktop", DefaultIdentityFileName);
    }

    private PrivacyDeviceIdentityDocument? TryLoad()
    {
        try
        {
            if (!File.Exists(_identityPath))
            {
                return null;
            }

            var json = File.ReadAllText(_identityPath);
            return JsonSerializer.Deserialize(
                json,
                PrivacyDeviceIdentityJsonContext.Default.PrivacyDeviceIdentityDocument);
        }
        catch
        {
            return null;
        }
    }

    private void Save(PrivacyDeviceIdentityDocument document)
    {
        var directory = Path.GetDirectoryName(_identityPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_identityPath}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(
            document,
            PrivacyDeviceIdentityJsonContext.Default.PrivacyDeviceIdentityDocument);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _identityPath, overwrite: true);
    }

    private static string GenerateDeviceId()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record PrivacyDeviceIdentityDocument(
        int SchemaVersion,
        string DeviceId,
        DateTimeOffset CreatedAtUtc);

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(PrivacyDeviceIdentityDocument))]
    private sealed partial class PrivacyDeviceIdentityJsonContext : JsonSerializerContext;
}
