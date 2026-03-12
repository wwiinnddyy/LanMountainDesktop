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
            var dialog = new ContentDialog
            {
                Title = L("single_instance.notice.title", "应用已经运行"),
                Content = L(
                    "single_instance.notice.description",
                    "应用已经运行，无需多次点击打开。"),
                PrimaryButtonText = L("single_instance.notice.button", "确定"),
                DefaultButton = ContentDialogButton.Primary
            };

            await dialog.ShowAsync(this);
        }
        finally
        {
            _isSingleInstancePromptVisible = false;
        }
    }
}
