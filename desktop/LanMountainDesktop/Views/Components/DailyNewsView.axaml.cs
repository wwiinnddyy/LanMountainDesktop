using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace LanMountainDesktop.Views.Components;

public partial class DailyNewsView : UserControl
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly JuyaDailyNews _news;
    private Bitmap? _coverBitmap;
    private bool _isNightMode;
    private bool _isExpanded;

    public event EventHandler? CoverImageClicked;
    public event EventHandler<string>? NewsItemClicked;

    public DailyNewsView(JuyaDailyNews news, bool isNightMode)
    {
        InitializeComponent();
        _news = news;
        _isNightMode = isNightMode;

        var dateStr = news.Date.ToString("yyyy年M月d日");
        var dayOfWeek = news.Date.ToString("dddd");
        DateTextBlock.Text = $"{dateStr} {dayOfWeek}";

        _ = LoadCoverImageAsync(news.CoverImageUrl);

        if (string.IsNullOrWhiteSpace(news.BilibiliUrl))
        {
            BilibiliButton.IsVisible = false;
        }

        if (string.IsNullOrWhiteSpace(news.IssueUrl))
        {
            WechatButton.IsVisible = false;
        }

        if (news.OverviewCategories.Any())
        {
            foreach (var category in news.OverviewCategories)
            {
                var categoryPanel = new StackPanel { Spacing = 6 };

                var categoryHeader = new TextBlock
                {
                    Text = $"{category.Icon} {category.Name}",
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(isNightMode ? Color.Parse("#d4736a") : Color.Parse("#bb5649"))
                };
                categoryPanel.Children.Add(categoryHeader);

                foreach (var item in category.Items)
                {
                    var itemPanel = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 4
                    };

                    var bulletText = new TextBlock
                    {
                        Text = "•",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(isNightMode ? Color.Parse("#9a9590") : Color.Parse("#757575"))
                    };
                    itemPanel.Children.Add(bulletText);

                    if (!string.IsNullOrWhiteSpace(item.Url))
                    {
                        var linkButton = new HyperlinkButton
                        {
                            Content = item.Title,
                            NavigateUri = new Uri(item.Url),
                            FontSize = 13,
                            Foreground = new SolidColorBrush(isNightMode ? Color.Parse("#9a9590") : Color.Parse("#757575")),
                            Padding = new Thickness(0)
                        };
                        itemPanel.Children.Add(linkButton);
                    }
                    else
                    {
                        var titleText = new TextBlock
                        {
                            Text = item.Title,
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(isNightMode ? Color.Parse("#9a9590") : Color.Parse("#757575"))
                        };
                        itemPanel.Children.Add(titleText);
                    }

                    if (item.Number.HasValue)
                    {
                        var numberText = new TextBlock
                        {
                            Text = $"#{item.Number}",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(isNightMode ? Color.Parse("#d4736a") : Color.Parse("#bb5649")),
                            FontWeight = FontWeight.SemiBold,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };
                        itemPanel.Children.Add(numberText);
                    }

                    categoryPanel.Children.Add(itemPanel);
                }

                OverviewStackPanel.Children.Add(categoryPanel);
            }
        }
        else
        {
            OverviewBorder.IsVisible = false;
        }

        if (!news.DetailedNews.Any())
        {
            ShowMoreButton.IsVisible = false;
        }
        else
        {
            foreach (var detailedItem in news.DetailedNews)
            {
                var newsPanel = CreateDetailedNewsPanel(detailedItem, isNightMode);
                DetailedNewsStackPanel.Children.Add(newsPanel);
            }
        }

        ApplyNightMode(isNightMode);
    }

    private Border CreateDetailedNewsPanel(JuyaDetailedNewsItem detailedItem, bool isNightMode)
    {
        var primaryColor = isNightMode ? "#d4736a" : "#bb5649";
        var textColor = isNightMode ? "#e8e4e0" : "#34495e";
        var secondaryTextColor = isNightMode ? "#9a9590" : "#757575";

        var mainBorder = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#e6e6e6")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 16)
        };

        var mainStack = new StackPanel { Spacing = 12 };
        mainBorder.Child = mainStack;

        var headerPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8
        };

        if (detailedItem.Number > 0)
        {
            var numberBadge = new Border
            {
                Background = new SolidColorBrush(Color.Parse(primaryColor)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            var numberText = new TextBlock
            {
                Text = $"#{detailedItem.Number}",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White
            };
            numberBadge.Child = numberText;
            headerPanel.Children.Add(numberBadge);
        }

        var titleText = new TextBlock
        {
            Text = detailedItem.Title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse(textColor)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        headerPanel.Children.Add(titleText);
        mainStack.Children.Add(headerPanel);

        if (!string.IsNullOrWhiteSpace(detailedItem.BodyText))
        {
            var bodyText = new TextBlock
            {
                Text = detailedItem.BodyText,
                FontSize = 14,
                LineHeight = 22,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse(textColor))
            };
            mainStack.Children.Add(bodyText);
        }

        if (detailedItem.RelatedLinks.Any())
        {
            var linksPanel = new StackPanel { Spacing = 4 };

            var linksHeader = new TextBlock
            {
                Text = "相关链接：",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse(secondaryTextColor))
            };
            linksPanel.Children.Add(linksHeader);

            foreach (var link in detailedItem.RelatedLinks.Take(3))
            {
                var linkButton = new HyperlinkButton
                {
                    Content = link.Length > 50 ? link.Substring(0, 50) + "..." : link,
                    NavigateUri = new Uri(link),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse(primaryColor))
                };
                linksPanel.Children.Add(linkButton);
            }

            mainStack.Children.Add(linksPanel);
        }

        return mainBorder;
    }

    private void OnShowMoreButtonClick(object? sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        DetailedNewsStackPanel.IsVisible = _isExpanded;
        ShowMoreButton.Content = _isExpanded ? "收起新闻 ▲" : "展开更多新闻 ▼";
    }

    private void OnBilibiliButtonClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_news.BilibiliUrl))
        {
            TryOpenUrl(_news.BilibiliUrl);
        }
        e.Handled = true;
    }

    private void OnWechatButtonClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_news.IssueUrl))
        {
            TryOpenUrl(_news.IssueUrl);
        }
        e.Handled = true;
    }

    private static void TryOpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch
        {
        }
    }

    private async Task LoadCoverImageAsync(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        try
        {
            using var response = await HttpClient.GetAsync(imageUrl);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                var bitmap = new Bitmap(stream);
                _coverBitmap = bitmap;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CoverImage.Source = bitmap;
                });
            }
        }
        catch
        {
        }
    }

    private void OnCoverImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CoverImageClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    public void ApplyNightMode(bool isNightMode)
    {
        _isNightMode = isNightMode;
        var primaryColor = isNightMode ? "#d4736a" : "#bb5649";
        var textColor = isNightMode ? "#e8e4e0" : "#34495e";
        var secondaryTextColor = isNightMode ? "#9a9590" : "#757575";
        var separatorColor = isNightMode ? "#3d3a3a" : "#e6e6e6";
        var coverBgColor = isNightMode ? "#3d3a3a" : "#f8f5ec";
        var overviewBgColor = isNightMode ? "#3d3a3a" : "#f8f5ec";

        DateTextBlock.Foreground = new SolidColorBrush(Color.Parse(primaryColor));
        DateSeparatorBorder.Background = new SolidColorBrush(Color.Parse(separatorColor));
        CoverImageBorder.Background = new SolidColorBrush(Color.Parse(coverBgColor));
        OverviewBorder.Background = new SolidColorBrush(Color.Parse(overviewBgColor));

        ShowMoreButton.BorderBrush = new SolidColorBrush(Color.Parse(primaryColor));
        ShowMoreButton.Foreground = new SolidColorBrush(Color.Parse(primaryColor));

        foreach (var child in OverviewStackPanel.Children)
        {
            if (child is StackPanel categoryPanel && categoryPanel.Children.Count > 0)
            {
                if (categoryPanel.Children[0] is TextBlock categoryHeader)
                {
                    categoryHeader.Foreground = new SolidColorBrush(Color.Parse(primaryColor));
                }

                for (int i = 1; i < categoryPanel.Children.Count; i++)
                {
                    if (categoryPanel.Children[i] is StackPanel itemPanel)
                    {
                        foreach (var itemChild in itemPanel.Children)
                        {
                            if (itemChild is TextBlock textBlock)
                            {
                                if (textBlock.Text.StartsWith("#"))
                                {
                                    textBlock.Foreground = new SolidColorBrush(Color.Parse(primaryColor));
                                }
                                else
                                {
                                    textBlock.Foreground = new SolidColorBrush(Color.Parse(secondaryTextColor));
                                }
                            }
                            else if (itemChild is HyperlinkButton linkBtn)
                            {
                                linkBtn.Foreground = new SolidColorBrush(Color.Parse(secondaryTextColor));
                            }
                        }
                    }
                }
            }
        }

        foreach (var child in DetailedNewsStackPanel.Children)
        {
            if (child is Border mainBorder && mainBorder.Child is StackPanel mainStack)
            {
                mainBorder.BorderBrush = new SolidColorBrush(Color.Parse(separatorColor));

                foreach (var stackChild in mainStack.Children)
                {
                    if (stackChild is StackPanel headerPanel)
                    {
                        foreach (var headerChild in headerPanel.Children)
                        {
                            if (headerChild is Border numberBadge && numberBadge.Child is TextBlock numberText)
                            {
                                numberBadge.Background = new SolidColorBrush(Color.Parse(primaryColor));
                            }
                            else if (headerChild is TextBlock titleText)
                            {
                                titleText.Foreground = new SolidColorBrush(Color.Parse(textColor));
                            }
                        }
                    }
                    else if (stackChild is TextBlock bodyText)
                    {
                        bodyText.Foreground = new SolidColorBrush(Color.Parse(textColor));
                    }
                    else if (stackChild is StackPanel linksPanel)
                    {
                        foreach (var linkChild in linksPanel.Children)
                        {
                            if (linkChild is TextBlock linksHeader)
                            {
                                linksHeader.Foreground = new SolidColorBrush(Color.Parse(secondaryTextColor));
                            }
                            else if (linkChild is HyperlinkButton linkButton)
                            {
                                linkButton.Foreground = new SolidColorBrush(Color.Parse(primaryColor));
                            }
                        }
                    }
                }
            }
        }
    }

    public void UpdateLayout(double scale, double availableWidth)
    {
        var coverHeight = availableWidth * 9 / 16;
        CoverImageBorder.Width = availableWidth;
        CoverImageBorder.Height = coverHeight;

        DateTextBlock.FontSize = Math.Clamp(20 * scale, 16, 26);

        ShowMoreButton.FontSize = Math.Clamp(14 * scale, 12, 16);

        var buttonSize = Math.Clamp(32 * scale, 24, 40);
        BilibiliButton.Width = buttonSize;
        BilibiliButton.Height = buttonSize;
        BilibiliButton.CornerRadius = new CornerRadius(buttonSize / 2);
        
        WechatButton.Width = buttonSize;
        WechatButton.Height = buttonSize;
        WechatButton.CornerRadius = new CornerRadius(buttonSize / 2);

        foreach (var child in OverviewStackPanel.Children)
        {
            if (child is StackPanel categoryPanel && categoryPanel.Children.Count > 0)
            {
                if (categoryPanel.Children[0] is TextBlock categoryHeader)
                {
                    categoryHeader.FontSize = Math.Clamp(15 * scale, 13, 18);
                }

                for (int i = 1; i < categoryPanel.Children.Count; i++)
                {
                    if (categoryPanel.Children[i] is StackPanel itemPanel)
                    {
                        foreach (var itemChild in itemPanel.Children)
                        {
                            if (itemChild is TextBlock textBlock)
                            {
                                textBlock.FontSize = Math.Clamp(13 * scale, 11, 15);
                            }
                            else if (itemChild is HyperlinkButton linkBtn)
                            {
                                linkBtn.FontSize = Math.Clamp(13 * scale, 11, 15);
                            }
                        }
                    }
                }
            }
        }

        foreach (var child in DetailedNewsStackPanel.Children)
        {
            if (child is Border mainBorder && mainBorder.Child is StackPanel mainStack)
            {
                foreach (var stackChild in mainStack.Children)
                {
                    if (stackChild is StackPanel headerPanel)
                    {
                        foreach (var headerChild in headerPanel.Children)
                        {
                            if (headerChild is Border numberBadge && numberBadge.Child is TextBlock numberText)
                            {
                                numberText.FontSize = Math.Clamp(12 * scale, 10, 14);
                            }
                            else if (headerChild is TextBlock titleText)
                            {
                                titleText.FontSize = Math.Clamp(16 * scale, 14, 20);
                            }
                        }
                    }
                    else if (stackChild is TextBlock bodyText)
                    {
                        bodyText.FontSize = Math.Clamp(14 * scale, 12, 16);
                        bodyText.LineHeight = 22 * scale;
                    }
                    else if (stackChild is StackPanel linksPanel)
                    {
                        foreach (var linkChild in linksPanel.Children)
                        {
                            if (linkChild is TextBlock linksHeader)
                            {
                                linksHeader.FontSize = Math.Clamp(12 * scale, 10, 14);
                            }
                            else if (linkChild is HyperlinkButton linkButton)
                            {
                                linkButton.FontSize = Math.Clamp(12 * scale, 10, 14);
                            }
                        }
                    }
                }
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _coverBitmap?.Dispose();
        _coverBitmap = null;
    }
}
