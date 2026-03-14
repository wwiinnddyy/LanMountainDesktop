namespace LanMountainDesktop.PluginSdk;

public interface IComponentEditorHostContext
{
    void RequestRefresh();

    void CloseEditor();

    void RequestRestart(string? reason = null);
}
