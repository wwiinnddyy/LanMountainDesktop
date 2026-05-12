using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LanMountainDesktop.Launcher.Views;

public partial class MultiInstancePromptWindow : Window
{
    private readonly TaskCompletionSource<MultiInstancePromptResult> _completionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _details = "LanMountain Desktop is already running.";

    public MultiInstancePromptWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnLoaded;
        Closed += (_, _) => _completionSource.TrySetResult(MultiInstancePromptResult.Close);
    }

    public Task<MultiInstancePromptResult> WaitForChoiceAsync() => _completionSource.Task;

    public void SetDetails(int processId, string shellState)
    {
        _details = $"Existing host PID: {processId}\nShell state: {shellState}\nNo second Host process was created.";

        if (this.FindControl<TextBlock>("DetailsText") is { } detailsText)
        {
            detailsText.Text = _details;
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Button>("CloseButton") is { } closeButton)
        {
            closeButton.Click += (_, _) => Complete(MultiInstancePromptResult.Close);
        }

        if (this.FindControl<Button>("OpenDesktopButton") is { } openDesktopButton)
        {
            openDesktopButton.Click += (_, _) => Complete(MultiInstancePromptResult.OpenDesktop);
        }

        if (this.FindControl<Button>("CopyDetailsButton") is { } copyDetailsButton)
        {
            copyDetailsButton.Click += OnCopyDetailsClick;
        }
    }

    private void Complete(MultiInstancePromptResult result)
    {
        _completionSource.TrySetResult(result);
        Close();
    }

    private async void OnCopyDetailsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is IClipboard clipboard)
            {
                await clipboard.SetTextAsync(_details);
            }
        }
        catch
        {
        }
    }
}

public enum MultiInstancePromptResult
{
    Close,
    OpenDesktop
}
