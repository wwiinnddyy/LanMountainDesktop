using System;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Markdown.Avalonia;

namespace LanMountainDesktop.Helpers;

public static class PluginCatalogMarkdownHelper
{
    private static Markdown.Avalonia.Markdown? _engine;

    public static ICommand OpenLinkCommand { get; } = new RelayCommand<object?>(OpenLink);

    public static Markdown.Avalonia.Markdown Engine => _engine ??= new Markdown.Avalonia.Markdown
    {
        HyperlinkCommand = OpenLinkCommand
    };

    private static void OpenLink(object? parameter)
    {
        var url = parameter switch
        {
            Uri uri => uri.ToString(),
            string text => text,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch failures inside the markdown viewer.
        }
    }
}
