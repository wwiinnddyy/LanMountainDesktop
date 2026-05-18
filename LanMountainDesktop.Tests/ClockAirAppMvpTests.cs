using System.Globalization;
using System.Text.Json;
using LanMountainDesktop.Services.ClockAirApp;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ClockAirAppMvpTests
{
    [Fact]
    public void SettingsSnapshot_DefaultsMatchClockSuiteMvp()
    {
        var snapshot = ClockAirAppSettingsSnapshot.Normalize(null);

        Assert.Equal(ClockAirAppTimeFormatMode.System, snapshot.TimeFormatMode);
        Assert.True(snapshot.ShowSeconds);
        Assert.Equal(ClockAirAppTabIds.Last, snapshot.StartupTab);
        Assert.Equal(ClockAirAppTabIds.WorldClock, snapshot.LastSelectedTab);
        Assert.True(snapshot.ActivateOnTimerFinished);
        Assert.Equal(4, snapshot.WorldClockTimeZoneIds.Count);
    }

    [Fact]
    public void SettingsStore_LoadsDefaultsWhenJsonIsBroken()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, "{ broken json");

        var store = new ClockAirAppSettingsStore(path);
        var snapshot = store.Load();

        Assert.Equal(ClockAirAppTimeFormatMode.System, snapshot.TimeFormatMode);
        Assert.Equal(4, snapshot.WorldClockTimeZoneIds.Count);
    }

    [Fact]
    public void SettingsStore_SavesAndLoadsIndependentClockSettings()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "settings.json");
        var store = new ClockAirAppSettingsStore(path);

        store.Save(new ClockAirAppSettingsSnapshot
        {
            TimeFormatMode = ClockAirAppTimeFormatMode.TwelveHour,
            ShowSeconds = false,
            StartupTab = ClockAirAppTabIds.Timer,
            LastSelectedTab = ClockAirAppTabIds.Stopwatch,
            ActivateOnTimerFinished = false,
            WorldClockTimeZoneIds = ["UTC"]
        });

        var loaded = store.Load();
        Assert.Equal(ClockAirAppTimeFormatMode.TwelveHour, loaded.TimeFormatMode);
        Assert.False(loaded.ShowSeconds);
        Assert.Equal(ClockAirAppTabIds.Timer, loaded.StartupTab);
        Assert.Equal(ClockAirAppTabIds.Stopwatch, loaded.LastSelectedTab);
        Assert.False(loaded.ActivateOnTimerFinished);
        Assert.Single(loaded.WorldClockTimeZoneIds);
    }

    [Fact]
    public void TimeFormatter_FormatsTimeAndOffsets()
    {
        var time = new DateTime(2026, 5, 18, 21, 7, 9);
        var settings = new ClockAirAppSettingsSnapshot
        {
            TimeFormatMode = ClockAirAppTimeFormatMode.TwentyFourHour,
            ShowSeconds = true
        };

        Assert.Equal("21:07:09", ClockAirAppTimeFormatter.FormatTime(time, settings, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("UTC+08:30", ClockAirAppTimeFormatter.FormatUtcOffset(TimeSpan.FromMinutes(510)));
        Assert.Equal("UTC-05:00", ClockAirAppTimeFormatter.FormatUtcOffset(TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void StopwatchState_StartPauseLapAndReset()
    {
        var state = new ClockAirAppStopwatchState();
        var start = DateTimeOffset.Parse("2026-05-18T12:00:00Z", CultureInfo.InvariantCulture);

        state.StartOrResume(start);
        Assert.True(state.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(5), state.GetElapsed(start.AddSeconds(5)));

        var lap = state.AddLap(start.AddSeconds(6));
        Assert.Equal(TimeSpan.FromSeconds(6), lap);
        Assert.Single(state.Laps);

        state.Pause(start.AddSeconds(8));
        Assert.False(state.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(8), state.GetElapsed(start.AddSeconds(20)));

        state.Reset();
        Assert.Equal(TimeSpan.Zero, state.GetElapsed(start.AddSeconds(30)));
        Assert.Empty(state.Laps);
    }

    [Fact]
    public void TimerState_StartPauseAndComplete()
    {
        var state = new ClockAirAppTimerState();
        var start = DateTimeOffset.Parse("2026-05-18T12:00:00Z", CultureInfo.InvariantCulture);

        state.SetDuration(TimeSpan.FromSeconds(10));
        state.StartOrResume(start);
        Assert.True(state.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(6), state.GetRemaining(start.AddSeconds(4)));

        state.Pause(start.AddSeconds(4));
        Assert.False(state.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(6), state.GetRemaining(start.AddSeconds(20)));

        state.StartOrResume(start.AddSeconds(20));
        Assert.False(state.Update(start.AddSeconds(25)));
        Assert.True(state.Update(start.AddSeconds(26)));
        Assert.True(state.IsCompleted);
        Assert.Equal(TimeSpan.Zero, state.GetRemaining(start.AddSeconds(26)));
    }

    [Fact]
    public void LocalizationFiles_ContainClockAirAppKeys()
    {
        var requiredKeys = new[]
        {
            "clockairapp.title",
            "clockairapp.tab.world",
            "clockairapp.tab.stopwatch",
            "clockairapp.tab.timer",
            "clockairapp.tab.settings",
            "clockairapp.settings.time_format.24h"
        };

        foreach (var language in new[] { "zh-CN", "en-US", "ja-JP", "ko-KR" })
        {
            var json = ReadRepositoryFile("LanMountainDesktop", "Localization", $"{language}.json");
            var table = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            Assert.NotNull(table);
            foreach (var key in requiredKeys)
            {
                Assert.True(table!.ContainsKey(key), $"{language} is missing {key}.");
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            if (File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                break;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}
