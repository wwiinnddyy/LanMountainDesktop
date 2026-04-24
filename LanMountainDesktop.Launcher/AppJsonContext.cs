using System.Text.Json;
using System.Text.Json.Serialization;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SignedFileMap))]
[JsonSerializable(typeof(UpdateFileEntry))]
[JsonSerializable(typeof(PlondsUpdateMetadata))]
[JsonSerializable(typeof(PlondsFileMap))]
[JsonSerializable(typeof(PlondsComponentEntry))]
[JsonSerializable(typeof(PlondsFileEntry))]
[JsonSerializable(typeof(PlondsHashDescriptor))]
[JsonSerializable(typeof(SnapshotMetadata))]
[JsonSerializable(typeof(AppVersionInfo))]
[JsonSerializable(typeof(StartupProgressMessage))]
[JsonSerializable(typeof(LauncherCoordinatorRequest))]
[JsonSerializable(typeof(LauncherCoordinatorResponse))]
[JsonSerializable(typeof(LauncherCoordinatorStatus))]
[JsonSerializable(typeof(PublicShellStatus))]
[JsonSerializable(typeof(PublicTrayStatus))]
[JsonSerializable(typeof(PublicTaskbarStatus))]
[JsonSerializable(typeof(PublicShellActivationResult))]
[JsonSerializable(typeof(LauncherResult))]
[JsonSerializable(typeof(HostDiscoveryConfig))]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(PendingUpgrade))]
[JsonSerializable(typeof(List<PendingUpgrade>))]
[JsonSerializable(typeof(OobeStateFile))]
[JsonSerializable(typeof(DataLocationConfig))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubAsset))]
[JsonSerializable(typeof(List<GitHubRelease>))]
[JsonSerializable(typeof(StartupAttemptRecord))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
