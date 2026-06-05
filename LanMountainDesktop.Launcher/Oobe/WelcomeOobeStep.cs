using Avalonia.Threading;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Oobe;

internal sealed class WelcomeOobeStep : IOobeStep
{
    private readonly CommandContext _context;
    private readonly OobeStateService _oobeStateService;
    private readonly DataLocationResolver _dataLocationResolver;

    public WelcomeOobeStep(
        OobeStateService oobeStateService,
        CommandContext context,
        DataLocationResolver dataLocationResolver)
    {
        _oobeStateService = oobeStateService;
        _context = context;
        _dataLocationResolver = dataLocationResolver;
    }

    public async Task<OobeStepResult> RunAsync(CancellationToken cancellationToken)
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
            return BuildCancelledResult("OOBE window could not be created.");
        }

        var draft = await window.WaitForCompletionAsync().ConfigureAwait(false);
        if (draft is null)
        {
            Logger.Info("OOBE was cancelled before completion; Host launch will be skipped.");
            return BuildCancelledResult("OOBE was cancelled before completion.");
        }

        var completion = new OobeSessionCommitService(
                _dataLocationResolver,
                _oobeStateService,
                _context)
            .Commit(draft);
        if (!completion.Success)
        {
            Logger.Warn(
                $"OOBE session was not persisted. ResultCode='{completion.ResultCode}'; " +
                $"Error='{completion.ErrorMessage}'.");
            return OobeStepResult.Complete(LaunchResultBuilder.Build(
                false,
                "oobe",
                completion.ResultCode,
                "OOBE settings could not be saved.",
                errorMessage: completion.ErrorMessage));
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (window.IsVisible)
            {
                window.Close();
            }
        });

        return OobeStepResult.Continue;
    }

    private static OobeStepResult BuildCancelledResult(string message)
    {
        return OobeStepResult.Complete(LaunchResultBuilder.Build(
            false,
            "oobe",
            "oobe_cancelled",
            message));
    }
}
