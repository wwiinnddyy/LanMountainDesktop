using System;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views;

namespace LanMountainDesktop.Services;

public readonly record struct ComponentEditorOpenRequest(
    Window Owner,
    DesktopComponentEditorDescriptor Descriptor,
    string ComponentId,
    string PlacementId,
    Action RefreshAction,
    Action<string?>? RestartAction = null);

public interface IComponentEditorWindowService
{
    bool IsOpen { get; }

    string? CurrentPlacementId { get; }

    void Open(ComponentEditorOpenRequest request);

    void Close();
}

internal sealed class ComponentEditorWindowService : IComponentEditorWindowService
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IMaterialColorService _materialColorService;
    private ComponentEditorWindow? _window;
    private string? _currentPlacementId;

    public ComponentEditorWindowService(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _materialColorService = HostMaterialColorProvider.GetOrCreate();
        _materialColorService.MaterialColorChanged += OnMaterialColorChanged;
    }

    public bool IsOpen => _window is { IsVisible: true };

    public string? CurrentPlacementId => _currentPlacementId;

    public void Open(ComponentEditorOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Owner);
        ArgumentNullException.ThrowIfNull(request.RefreshAction);

        _window ??= CreateWindow();

        var settingsService = _settingsFacade.Settings;
        var accessor = settingsService.GetComponentAccessor(request.ComponentId, request.PlacementId);
        var scopedStore = new ComponentSettingsService(settingsService);
        scopedStore.SetScopedComponentContext(request.ComponentId, request.PlacementId);

        var hostContext = new HostContext(this, request.RefreshAction, request.RestartAction);
        var context = new DesktopComponentEditorContext(
            request.Descriptor.Definition,
            request.ComponentId,
            request.PlacementId,
            _settingsFacade,
            settingsService,
            accessor,
            scopedStore,
            hostContext);

        _currentPlacementId = request.PlacementId;
        _window.ApplyDescriptor(request.Descriptor, context);

        if (!_window.IsVisible)
        {
            _window.Show(request.Owner);
            return;
        }

        _window.Activate();
    }

    public void Close()
    {
        _window?.Close();
    }

    private ComponentEditorWindow CreateWindow()
    {
        var window = new ComponentEditorWindow();
        ApplyTheme(window);
        window.ShowInTaskbar = false;
        window.Closed += (_, _) =>
        {
            _window = null;
            _currentPlacementId = null;
        };
        return window;
    }

    private void ApplyTheme(ComponentEditorWindow window)
    {
        var snapshot = _materialColorService.GetMaterialColorSnapshot();
        var palette = ComponentEditorMaterialThemeAdapter.Build(snapshot);

        window.ApplyTheme(palette);
        window.ApplyChromeMode(snapshot.UseSystemChrome);
        _materialColorService.ApplyWindowMaterial(window, MaterialSurfaceRole.WindowBackground);
    }

    private void OnMaterialColorChanged(object? sender, MaterialColorSnapshot snapshot)
    {
        _ = sender;

        if (_window is null)
        {
            return;
        }

        var palette = ComponentEditorMaterialThemeAdapter.Build(snapshot);
        _window.ApplyTheme(palette);
        _window.ApplyChromeMode(snapshot.UseSystemChrome);
        _materialColorService.ApplyWindowMaterial(_window, MaterialSurfaceRole.WindowBackground);
    }

    private sealed class HostContext : IComponentEditorHostContext
    {
        private readonly ComponentEditorWindowService _owner;
        private readonly Action _refreshAction;
        private readonly Action<string?>? _restartAction;

        public HostContext(
            ComponentEditorWindowService owner,
            Action refreshAction,
            Action<string?>? restartAction)
        {
            _owner = owner;
            _refreshAction = refreshAction;
            _restartAction = restartAction;
        }

        public void RequestRefresh()
        {
            _refreshAction();
        }

        public void CloseEditor()
        {
            _owner.Close();
        }

        public void RequestRestart(string? reason = null)
        {
            _restartAction?.Invoke(reason);
        }
    }
}
