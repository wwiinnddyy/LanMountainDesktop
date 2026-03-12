using Avalonia.Controls;

namespace LanMountainDesktop.PluginSdk;

public sealed class PluginSettingsPageRegistration
{
    public PluginSettingsPageRegistration(
        string id,
        string title,
        Func<IServiceProvider, Control> contentFactory,
        int sortOrder = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(contentFactory);

        Id = id.Trim();
        Title = title.Trim();
        ContentFactory = contentFactory;
        SortOrder = sortOrder;
    }

    public PluginSettingsPageRegistration(
        string id,
        string title,
        Func<Control> contentFactory,
        int sortOrder = 0)
        : this(id, title, _ => contentFactory(), sortOrder)
    {
    }

    public string Id { get; }

    public string Title { get; }

    public int SortOrder { get; }

    public Func<IServiceProvider, Control> ContentFactory { get; }
}
