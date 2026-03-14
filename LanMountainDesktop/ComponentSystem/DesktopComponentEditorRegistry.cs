using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ComponentSystem;

public sealed record DesktopComponentEditorContext(
    DesktopComponentDefinition Definition,
    string ComponentId,
    string? PlacementId,
    ISettingsFacadeService SettingsFacade,
    ISettingsService SettingsService,
    IComponentSettingsAccessor ComponentSettingsAccessor,
    IComponentInstanceSettingsStore ComponentSettingsStore,
    IComponentEditorHostContext HostContext);

public sealed class DesktopComponentEditorRegistration
{
    public DesktopComponentEditorRegistration(
        string componentId,
        Func<DesktopComponentEditorContext, Control> editorFactory,
        double preferredWidth = 720d,
        double preferredHeight = 540d,
        double minScale = 0.85d,
        double maxScale = 1.45d)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(editorFactory);

        if (preferredWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredWidth));
        }

        if (preferredHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredHeight));
        }

        if (minScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minScale));
        }

        if (maxScale < minScale)
        {
            throw new ArgumentOutOfRangeException(nameof(maxScale));
        }

        ComponentId = componentId.Trim();
        EditorFactory = editorFactory;
        PreferredWidth = preferredWidth;
        PreferredHeight = preferredHeight;
        MinScale = minScale;
        MaxScale = maxScale;
        AspectRatio = preferredWidth / preferredHeight;
    }

    public string ComponentId { get; }

    public Func<DesktopComponentEditorContext, Control> EditorFactory { get; }

    public double PreferredWidth { get; }

    public double PreferredHeight { get; }

    public double MinScale { get; }

    public double MaxScale { get; }

    public double AspectRatio { get; }
}

public sealed class DesktopComponentEditorDescriptor
{
    internal DesktopComponentEditorDescriptor(
        DesktopComponentDefinition definition,
        Func<DesktopComponentEditorContext, Control> editorFactory,
        double preferredWidth,
        double preferredHeight,
        double minScale,
        double maxScale,
        double aspectRatio)
    {
        Definition = definition;
        _editorFactory = editorFactory;
        PreferredWidth = preferredWidth;
        PreferredHeight = preferredHeight;
        MinScale = minScale;
        MaxScale = maxScale;
        AspectRatio = aspectRatio;
    }

    private readonly Func<DesktopComponentEditorContext, Control> _editorFactory;

    public DesktopComponentDefinition Definition { get; }

    public double PreferredWidth { get; }

    public double PreferredHeight { get; }

    public double MinScale { get; }

    public double MaxScale { get; }

    public double AspectRatio { get; }

    public Control CreateEditor(DesktopComponentEditorContext context)
    {
        return _editorFactory(context);
    }
}

public sealed class DesktopComponentEditorRegistry
{
    private readonly Dictionary<string, DesktopComponentEditorDescriptor> _descriptors;

    public DesktopComponentEditorRegistry(
        ComponentRegistry componentRegistry,
        IEnumerable<DesktopComponentEditorRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(componentRegistry);
        ArgumentNullException.ThrowIfNull(registrations);

        _descriptors = new Dictionary<string, DesktopComponentEditorDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            if (!componentRegistry.TryGetDefinition(registration.ComponentId, out var definition))
            {
                continue;
            }

            _descriptors[registration.ComponentId] = new DesktopComponentEditorDescriptor(
                definition,
                registration.EditorFactory,
                registration.PreferredWidth,
                registration.PreferredHeight,
                registration.MinScale,
                registration.MaxScale,
                registration.AspectRatio);
        }
    }

    public bool TryGetDescriptor(string componentId, out DesktopComponentEditorDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        return _descriptors.TryGetValue(componentId.Trim(), out descriptor!);
    }

    public IReadOnlyList<DesktopComponentEditorDescriptor> GetAll()
    {
        return _descriptors.Values.ToList();
    }
}
