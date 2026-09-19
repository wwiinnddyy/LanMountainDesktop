using System.Text.Json;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// settings.json 的旧键（DisabledPluginIds / DevPluginPath）迁移契约。
/// 这里的价值全在"老用户升级后设置不丢、且旧键不会回写"，所以断言都针对真实 JSON 文本。
/// </summary>
public sealed class AppSettingsLegacyKeyMigrationTests
{
    private static readonly JsonSerializerOptions ReadOptions = new() { WriteIndented = true };

    [Fact]
    public void LegacyOnlySettings_MigrateIntoNewMembers()
    {
        var snapshot = Deserialize("""
            {
              "DisabledPluginIds": [ "com.lanword.lookup", "com.voicehub.landesktop" ],
              "DevPluginPath": "C:\\work\\LanMountainDesktop.SamplePlugin\\bin\\Debug\\net10.0"
            }
            """);

        Assert.Empty(snapshot.DisabledAirAppIds);
        Assert.Null(snapshot.DevAirAppPath);

        Assert.True(AppSettingsService.TryMergeLegacySettingsKeys(snapshot));

        Assert.Equal(
            new[] { "com.lanword.lookup", "com.voicehub.landesktop" },
            snapshot.DisabledAirAppIds);
        Assert.Equal(
            @"C:\work\LanMountainDesktop.SamplePlugin\bin\Debug\net10.0",
            snapshot.DevAirAppPath);
    }

    [Fact]
    public void BothKeyPresent_NewKeyWins()
    {
        var snapshot = Deserialize("""
            {
              "DisabledAirAppIds": [ "app.user.chose.after.upgrade" ],
              "DisabledPluginIds": [ "app.from.old.install" ]
            }
            """);

        Assert.True(AppSettingsService.TryMergeLegacySettingsKeys(snapshot));

        Assert.Equal(new[] { "app.user.chose.after.upgrade" }, snapshot.DisabledAirAppIds);
    }

    [Fact]
    public void MigratedSnapshot_NoLongerSerializesLegacyKeys()
    {
        var snapshot = Deserialize("""
            {
              "DisabledPluginIds": [ "com.sample.airapp" ],
              "DevPluginPath": "C:\\dev\\sample"
            }
            """);

        Assert.Contains("DisabledPluginIds", JsonSerializer.Serialize(snapshot, ReadOptions));

        Assert.True(AppSettingsService.TryMergeLegacySettingsKeys(snapshot));

        var persisted = JsonSerializer.Serialize(snapshot, ReadOptions);

        Assert.DoesNotContain("DisabledPluginIds", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("DevPluginPath", persisted, StringComparison.Ordinal);
        Assert.Contains("DisabledAirAppIds", persisted, StringComparison.Ordinal);
        Assert.Contains("DevAirAppPath", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_IsIdempotentAndSilentWhenNoLegacyKeys()
    {
        var snapshot = Deserialize("""
            {
              "DisabledAirAppIds": [ "com.sample.airapp" ]
            }
            """);

        Assert.False(AppSettingsService.TryMergeLegacySettingsKeys(snapshot));

        snapshot.DisabledAirAppIds.Add("com.another.airapp");
        Assert.False(AppSettingsService.TryMergeLegacySettingsKeys(snapshot));
        Assert.Equal(2, snapshot.DisabledAirAppIds.Count);
    }

    [Fact]
    public void Clone_KeepsNewListDetachedAndDropsMergedAlias()
    {
        var snapshot = Deserialize("""
            {
              "DisabledPluginIds": [ "com.sample.airapp" ]
            }
            """);
        AppSettingsService.TryMergeLegacySettingsKeys(snapshot);

        var clone = snapshot.Clone();
        clone.DisabledAirAppIds.Add("com.extra.airapp");

        Assert.Single(snapshot.DisabledAirAppIds);
        Assert.Null(clone.LegacyDisabledPluginIds);
    }

    [Fact]
    public void Clone_PreservesUnmergedAliasSoItIsNotLostBeforePersist()
    {
        var snapshot = Deserialize("""
            {
              "DisabledPluginIds": [ "com.sample.airapp" ]
            }
            """);

        var clone = snapshot.Clone();

        Assert.Equal(new[] { "com.sample.airapp" }, clone.LegacyDisabledPluginIds);
    }

    private static AppSettingsSnapshot Deserialize(string json)
    {
        return JsonSerializer.Deserialize<AppSettingsSnapshot>(json, ReadOptions)
            ?? new AppSettingsSnapshot();
    }
}
