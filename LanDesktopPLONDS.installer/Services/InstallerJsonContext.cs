using System.Text.Json.Serialization;

namespace LanDesktopPLONDS.Installer.Services;

[JsonSerializable(typeof(InstallerPlondsManifest))]
internal sealed partial class InstallerJsonContext : JsonSerializerContext;
