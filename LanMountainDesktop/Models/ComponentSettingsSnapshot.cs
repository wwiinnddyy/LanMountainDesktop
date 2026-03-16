using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public sealed class ComponentSettingsSnapshot
{
    public string DailyArtworkMirrorSource { get; set; } = DailyArtworkMirrorSources.Overseas;

    public string? ColorSchemeSource { get; set; }

    public List<ImportedClassScheduleSnapshot> ImportedClassSchedules { get; set; } = [];

    public string ActiveImportedClassScheduleId { get; set; } = string.Empty;

    public bool StudyEnvironmentShowDisplayDb { get; set; } = true;

    public bool StudyEnvironmentShowDbfs { get; set; }

    public string DesktopClockTimeZoneId { get; set; } = "China Standard Time";

    public string DesktopClockSecondHandMode { get; set; } = "Tick";

    public List<string> WorldClockTimeZoneIds { get; set; } =
    [
        "China Standard Time",
        "GMT Standard Time",
        "AUS Eastern Standard Time",
        "Eastern Standard Time"
    ];

    public string WorldClockSecondHandMode { get; set; } = "Tick";

    public bool CnrDailyNewsAutoRotateEnabled { get; set; } = true;

    public int CnrDailyNewsAutoRotateIntervalMinutes { get; set; } = 60;

    public bool IfengNewsAutoRefreshEnabled { get; set; } = true;

    public int IfengNewsAutoRefreshIntervalMinutes { get; set; } = 20;

    public string IfengNewsChannelType { get; set; } = IfengNewsChannelTypes.Comprehensive;

    public bool DailyWordAutoRefreshEnabled { get; set; } = true;

    public int DailyWordAutoRefreshIntervalMinutes { get; set; } = 360;

    public bool BilibiliHotSearchAutoRefreshEnabled { get; set; } = true;

    public int BilibiliHotSearchAutoRefreshIntervalMinutes { get; set; } = 15;

    public bool BaiduHotSearchAutoRefreshEnabled { get; set; } = true;

    public int BaiduHotSearchAutoRefreshIntervalMinutes { get; set; } = 15;

    public string BaiduHotSearchSourceType { get; set; } = BaiduHotSearchSourceTypes.Official;

    public bool WeatherAutoRefreshEnabled { get; set; } = true;

    public int WeatherAutoRefreshIntervalMinutes { get; set; } = 12;

    public bool Stcn24ForumAutoRefreshEnabled { get; set; } = true;

    public int Stcn24ForumAutoRefreshIntervalMinutes { get; set; } = 20;

    public string Stcn24ForumSourceType { get; set; } = Stcn24ForumSourceTypes.LatestCreated;

    public ComponentSettingsSnapshot Clone()
    {
        var clone = (ComponentSettingsSnapshot)MemberwiseClone();

        var schedules = new List<ImportedClassScheduleSnapshot>(ImportedClassSchedules?.Count ?? 0);
        if (ImportedClassSchedules is not null)
        {
            foreach (var schedule in ImportedClassSchedules)
            {
                if (schedule is null)
                {
                    continue;
                }

                schedules.Add(new ImportedClassScheduleSnapshot
                {
                    Id = schedule.Id,
                    DisplayName = schedule.DisplayName,
                    FilePath = schedule.FilePath
                });
            }
        }

        clone.ImportedClassSchedules = schedules;
        clone.WorldClockTimeZoneIds = WorldClockTimeZoneIds is { Count: > 0 }
            ? new List<string>(WorldClockTimeZoneIds)
            : [];

        return clone;
    }
}
