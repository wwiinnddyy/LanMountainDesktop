using System.Text.Json.Serialization;

namespace LanMountainDesktop.Shared.Contracts.Deployment;

internal sealed record DeploymentMaintenanceRequest(
    string InstallRoot, string? ManifestPath, string? ArchivePath, string? ArchiveSha256,
    string? Version, int ParentProcessId, long ParentStartTimeUtcTicks, string ReceiptPath,
    string? RollbackSnapshotPath = null, bool Restart = true);

internal sealed record DeploymentMaintenanceReceipt(string Status, string? Version, string? Error);

internal static class DeploymentMaintenance
{
    internal const string ExecutableName = "LanDesktopPLONDS.installer.exe";
    internal const string RequestFileName = "maintenance-request.json";
    internal const string ReceiptFileName = "maintenance-receipt.json";
}

[JsonSerializable(typeof(DeploymentMaintenanceRequest))]
[JsonSerializable(typeof(DeploymentMaintenanceReceipt))]
internal partial class DeploymentMaintenanceJsonContext : JsonSerializerContext;
