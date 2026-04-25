using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class DataLocationOobeStep : IOobeStep
{
    private readonly DataLocationResolver _resolver;

    public DataLocationOobeStep(DataLocationResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existingConfig = _resolver.LoadConfig();
        if (existingConfig is not null)
        {
            Logger.Info("DataLocation OOBE step skipped: config already exists.");
            return;
        }

        DataLocationPromptWindow? window = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            window = new DataLocationPromptWindow(_resolver);
            window.Show();
        });

        if (window is null)
        {
            Logger.Warn("DataLocation OOBE step failed: window could not be created.");
            return;
        }

        try
        {
            var result = await window.WaitForChoiceAsync().ConfigureAwait(false);
            if (result is null)
            {
                Logger.Info("DataLocation OOBE step: user cancelled or closed window. Using default system location.");
                _resolver.ApplyLocationChoice(DataLocationMode.System, null, false);
            }
            else
            {
                var success = _resolver.ApplyLocationChoice(result.SelectedMode, null, result.MigrateExistingData);
                Logger.Info(
                    $"DataLocation OOBE step: user selected '{result.SelectedMode}'. " +
                    $"Migrate={result.MigrateExistingData}; Success={success}.");
            }
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            });
        }
    }
}
