using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class NotificationListenerServiceTests
{
    [Fact]
    public void AddNotification_DeduplicatesByPlatformAndSourceId()
    {
        var settings = new FakeSettingsService();
        var service = new NotificationListenerService(settings);

        service.AddNotification(new NotificationItem
        {
            Platform = "Windows",
            SourceNotificationId = "42",
            AppId = "mail",
            AppName = "Mail",
            Title = "First"
        });
        service.AddNotification(new NotificationItem
        {
            Platform = "Windows",
            SourceNotificationId = "42",
            AppId = "mail",
            AppName = "Mail",
            Title = "Updated"
        });

        var notification = Assert.Single(service.GetNotifications());
        Assert.Equal("Updated", notification.Title);
    }

    [Fact]
    public void AddNotification_RespectsBlockedApps()
    {
        var settings = new FakeSettingsService();
        settings.Snapshot.NotificationBoxBlockedApps.Add("blocked-app");
        var service = new NotificationListenerService(settings);

        service.AddNotification(new NotificationItem
        {
            AppId = "blocked-app",
            AppName = "Blocked",
            Title = "Hidden"
        });

        Assert.Empty(service.GetNotifications());
    }

    [Fact]
    public void AddNotification_TrimsToMaxStoredCount()
    {
        var settings = new FakeSettingsService();
        settings.Snapshot.NotificationBoxMaxStoredCount = 2;
        var service = new NotificationListenerService(settings);

        service.AddNotification(new NotificationItem { AppId = "a", AppName = "A", Title = "1" });
        service.AddNotification(new NotificationItem { AppId = "b", AppName = "B", Title = "2" });
        service.AddNotification(new NotificationItem { AppId = "c", AppName = "C", Title = "3" });

        var notifications = service.GetNotifications();
        Assert.Equal(2, notifications.Count);
        Assert.DoesNotContain(notifications, n => n.Title == "1");
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettingsSnapshot Snapshot { get; } = new();

        public event EventHandler<SettingsChangedEvent>? Changed;

        public T LoadSnapshot<T>(SettingsScope scope, string? subjectId = null, string? placementId = null) where T : new()
            => typeof(T) == typeof(AppSettingsSnapshot)
                ? (T)(object)Snapshot
                : new T();

        public void SaveSnapshot<T>(SettingsScope scope, T snapshot, string? subjectId = null, string? placementId = null, string? sectionId = null, IReadOnlyCollection<string>? changedKeys = null)
        {
        }

        public T LoadSection<T>(SettingsScope scope, string subjectId, string sectionId, string? placementId = null) where T : new()
            => new();

        public void SaveSection<T>(SettingsScope scope, string subjectId, string sectionId, T section, string? placementId = null, IReadOnlyCollection<string>? changedKeys = null)
        {
        }

        public void DeleteSection(SettingsScope scope, string subjectId, string sectionId, string? placementId = null)
        {
        }

        public T? GetValue<T>(SettingsScope scope, string key, string? subjectId = null, string? placementId = null, string? sectionId = null)
            => default;

        public void SetValue<T>(SettingsScope scope, string key, T value, string? subjectId = null, string? placementId = null, string? sectionId = null, IReadOnlyCollection<string>? changedKeys = null)
        {
        }

        public IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId)
            => throw new NotSupportedException();
    }
}
