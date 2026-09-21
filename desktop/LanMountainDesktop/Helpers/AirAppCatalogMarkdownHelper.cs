using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Markdown.Avalonia;

namespace LanMountainDesktop.Helpers;

public static class AirAppCatalogMarkdownHelper
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

        // 目录页渲染的是第三方 AirApp 自带的 README，只放行 http/https。
        ExternalLinkLauncher.TryOpen(url);
    }
}
