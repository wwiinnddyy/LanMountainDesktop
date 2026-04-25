namespace LanMountainDesktop.Launcher.Models;

internal enum DataLocationMode
{
    System,
    Portable
}

internal sealed class DataLocationConfig
{
    public string DataLocationMode { get; set; } = "System";

    public string? SystemDataPath { get; set; }

    public string? PortableDataPath { get; set; }
}

internal sealed class DataLocationPromptResult
{
    public DataLocationMode SelectedMode { get; init; }

    public bool MigrateExistingData { get; init; }
}
