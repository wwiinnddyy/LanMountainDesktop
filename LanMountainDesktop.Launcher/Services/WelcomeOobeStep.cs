using Avalonia.Threading;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class WelcomeOobeStep : IOobeStep
{
    private readonly OobeStateService _oobeStateService;

    public WelcomeOobeStep(OobeStateService oobeStateService)
    {
        _oobeStateService = oobeStateService;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        OobeWindow? window = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            window = new OobeWindow();
            window.Show();
        });

        if (window is null)
        {
            return;
        }

        await window.WaitForEnterAsync().ConfigureAwait(false);
        _oobeStateService.MarkCompleted();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (window.IsVisible)
            {
                window.Close();
            }
        });
    }
}
