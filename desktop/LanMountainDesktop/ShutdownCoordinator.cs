using System;

namespace LanMountainDesktop.DesktopHost;

public sealed class ShutdownCoordinator
{
    private readonly Action<bool, string> _prepareForShutdown;
    private readonly Action<string> _resetShutdownIntent;

    public ShutdownCoordinator(Action<bool, string> prepareForShutdown, Action<string> resetShutdownIntent)
    {
        _prepareForShutdown = prepareForShutdown ?? throw new ArgumentNullException(nameof(prepareForShutdown));
        _resetShutdownIntent = resetShutdownIntent ?? throw new ArgumentNullException(nameof(resetShutdownIntent));
    }

    public void Prepare(bool isRestart, string source) => _prepareForShutdown(isRestart, source);

    public void Reset(string source) => _resetShutdownIntent(source);
}
