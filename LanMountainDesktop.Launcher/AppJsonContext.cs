using System.Text.Json;
using System.Text.Json.Serialization;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SignedFileMap))]
[JsonSerializable(typeof(UpdateFileEntry))]
[JsonSerializable(typeof(SnapshotMetadata))]
[JsonSerializable(typeof(AppVersionInfo))]
[JsonSerializable(typeof(StartupProgressMessage))]
[JsonSerializable(typeof(LauncherResult))]
[JsonSerializable(typeof(HostDiscoveryConfig))]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(PendingUpgrade))]
[JsonSerializable(typeof(List<PendingUpgrade>))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubAsset))]
[JsonSerializable(typeof(List<GitHubRelease>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
