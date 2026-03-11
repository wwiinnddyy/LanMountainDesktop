using System;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private readonly DispatcherTimer _singleInstanceNoticeTimer = new()
    {
        Interval = TimeSpan.FromSeconds(6)
    };

    internal void ShowSingleInstanceNotice()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowSingleInstanceNoticeCore();
            return;
        }

        Dispatcher.UIThread.Post(ShowSingleInstanceNoticeCore, DispatcherPriority.Send);
    }

    private void ShowSingleInstanceNoticeCore()
    {
        SingleInstanceNoticeTitleTextBlock.Text = L(
            "single_instance.notice.title",
            "App already open");
        SingleInstanceNoticeDescriptionTextBlock.Text = L(
            "single_instance.notice.description",
            "LanMountainDesktop is already running. Switched back to the active desktop.");
        SingleInstanceNoticeButtonTextBlock.Text = L(
            "single_instance.notice.button",
            "Got it");
        SingleInstanceNoticeDock.IsVisible = true;

        _singleInstanceNoticeTimer.Stop();
        _singleInstanceNoticeTimer.Tick -= OnSingleInstanceNoticeTimerTick;
        _singleInstanceNoticeTimer.Tick += OnSingleInstanceNoticeTimerTick;
        _singleInstanceNoticeTimer.Start();
    }

    private void OnSingleInstanceNoticeButtonClick(object? sender, RoutedEventArgs e)
    {
        HideSingleInstanceNotice();
    }

    private void OnSingleInstanceNoticeTimerTick(object? sender, EventArgs e)
    {
        HideSingleInstanceNotice();
    }

    private void HideSingleInstanceNotice()
    {
        _singleInstanceNoticeTimer.Stop();
        SingleInstanceNoticeDock.IsVisible = false;
    }
}
