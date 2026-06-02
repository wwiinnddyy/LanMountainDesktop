using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanDesktopPLONDS.Installer.Services;

public sealed partial class InstallerPrivacyConsentStore
{
    private const string ConsentFileName = "privacy-consent.json";

    private readonly string _consentPath;
    private readonly object _gate = new();

    public InstallerPrivacyConsentStore(string? consentPath = null)
    {
        _consentPath = string.IsNullOrWhiteSpace(consentPath)
            ? GetDefaultConsentPath()
            : Path.GetFullPath(consentPath);
    }

    public bool HasConfirmed(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        lock (_gate)
        {
            var document = TryLoad();
            return document is not null
                   && string.Equals(document.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                   && document.ConfirmedAtUtc <= DateTimeOffset.UtcNow;
        }
    }

    public void SaveConfirmed(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("Device ID is required.", nameof(deviceId));
        }

        lock (_gate)
        {
            Save(new InstallerPrivacyConsentDocument(
                SchemaVersion: 1,
                DeviceId: deviceId,
                ConfirmedAtUtc: DateTimeOffset.UtcNow,
                Categories:
                [
                    "anonymousDeviceId",
                    "systemAndArchitecture",
                    "targetVersion",
                    "serverReceivedIpAddress"
                ]));
        }
    }

    public static string GetDefaultConsentPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        return Path.Combine(root, "LanMountainDesktop", "Installer", ConsentFileName);
    }

    private InstallerPrivacyConsentDocument? TryLoad()
    {
        try
        {
            if (!File.Exists(_consentPath))
            {
                return null;
            }

            var json = File.ReadAllText(_consentPath);
            return JsonSerializer.Deserialize(
                json,
                InstallerPrivacyConsentJsonContext.Default.InstallerPrivacyConsentDocument);
        }
        catch
        {
            return null;
        }
    }

    private void Save(InstallerPrivacyConsentDocument document)
    {
        var directory = Path.GetDirectoryName(_consentPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_consentPath}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(
            document,
            InstallerPrivacyConsentJsonContext.Default.InstallerPrivacyConsentDocument);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _consentPath, overwrite: true);
    }

    private sealed record InstallerPrivacyConsentDocument(
        int SchemaVersion,
        string DeviceId,
        DateTimeOffset ConfirmedAtUtc,
        IReadOnlyList<string> Categories);

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(InstallerPrivacyConsentDocument))]
    private sealed partial class InstallerPrivacyConsentJsonContext : JsonSerializerContext;
}
