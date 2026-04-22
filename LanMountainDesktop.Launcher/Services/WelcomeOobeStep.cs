using Avalonia.Threading;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class WelcomeOobeStep : IOobeStep
{
    private readonly CommandContext _context;
    private readonly OobeStateService _oobeStateService;

    public WelcomeOobeStep(OobeStateService oobeStateService, CommandContext context)
    {
        _oobeStateService = oobeStateService;
        _context = context;
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
        var completion = _oobeStateService.MarkCompleted(_context);
        if (!completion.Success)
        {
            Logger.Warn(
                $"OOBE completion state was not persisted. ResultCode='{completion.ResultCode}'; " +
                $"Error='{completion.ErrorMessage}'.");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (window.IsVisible)
            {
                window.Close();
            }
        });
    }
}
