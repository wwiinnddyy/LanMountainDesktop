using System.Text.Json;
using System.Text.Json.Serialization;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Plugins;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
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
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(List<PendingUpgrade>))]
[JsonSerializable(typeof(OobeStateFile))]
[JsonSerializable(typeof(DataLocationConfig))]
[JsonSerializable(typeof(StartupAttemptRecord))]
[JsonSerializable(typeof(PrivacyConfig))]
[JsonSerializable(typeof(PrivacyAgreementState))]
[JsonSerializable(typeof(AirAppOpenRequest))]
[JsonSerializable(typeof(AirAppRegistrationRequest))]
[JsonSerializable(typeof(AirAppInstanceInfo))]
[JsonSerializable(typeof(AirAppOperationResult))]
[JsonSerializable(typeof(AirAppInstanceInfo[]))]
[JsonSerializable(typeof(AirAppRuntimeControlResult))]
[JsonSerializable(typeof(AirAppRuntimeStatus))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
