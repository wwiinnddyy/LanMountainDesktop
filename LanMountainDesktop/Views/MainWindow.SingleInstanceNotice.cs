using System.Threading.Tasks;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private bool _isSingleInstancePromptVisible;

    internal void ShowSingleInstanceNotice()
    {
        void ShowPrompt()
        {
            UiExceptionGuard.FireAndForgetGuarded(
                ShowSingleInstanceNoticeCoreAsync,
                "MainWindow.ShowSingleInstanceNotice");
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowPrompt();
            return;
        }

        Dispatcher.UIThread.Post(ShowPrompt, DispatcherPriority.Send);
    }

    private async Task ShowSingleInstanceNoticeCoreAsync()
    {
        if (_isSingleInstancePromptVisible)
        {
            return;
        }

        _isSingleInstancePromptVisible = true;

        try
        {
            var dialog = new FAContentDialog
            {
                Title = L("single_instance.notice.title", "Already running"),
                Content = L(
                    "single_instance.notice.description",
                    "LanMountainDesktop is already running. The existing window will stay active, so no new instance was started."),
                PrimaryButtonText = L("single_instance.notice.button", "OK"),
                DefaultButton = FAContentDialogButton.Primary
            };

            await dialog.ShowAsync(this);
        }
        finally
        {
            _isSingleInstancePromptVisible = false;
        }
    }
}
