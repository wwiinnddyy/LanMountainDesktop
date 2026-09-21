namespace LanMountainDesktop.Launcher.Models;

using LanMountainDesktop.Shared.Contracts.Data;
using System.Text.Json.Serialization;

internal enum DataLocationMode
{
    System,
    Portable
}

internal sealed class DataLocationConfig
{
    [JsonPropertyName(DataLocationContract.ModePropertyName)]
    public string DataLocationMode { get; set; } = DataLocationContract.SystemModeValue;

    [JsonPropertyName(DataLocationContract.SystemPathPropertyName)]
    public string? SystemDataPath { get; set; }

    [JsonPropertyName(DataLocationContract.PortablePathPropertyName)]
    public string? PortableDataPath { get; set; }
}

internal sealed class DataLocationPromptResult
{
    public DataLocationMode SelectedMode { get; init; }

    public bool MigrateExistingData { get; init; }
}
