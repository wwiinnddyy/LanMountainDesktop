using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class JuyaNewsWidget : UserControl, IDesktopComponentWidget
{
    private static readonly FontFamily MiSansFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private const string RssUrl = "https://imjuya.github.io/juya-ai-daily/rss.xml";
    private const double BaseCellSize = 48d;
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 4;
    private const int InitialLoadDays = 3;
    private const int LoadMoreDays = 3;
    private const int MaxCachedDays = 30;

    private readonly Dictionary<DateTime, JuyaDailyNews> _cachedNews = new();
    private readonly List<DateTime> _loadedDates = new();
    private readonly List<DailyNewsView> _dailyViews = new();
    
    private double _currentCellSize = BaseCellSize;
    private bool _isAttached;
    private bool _isLoading;
    private bool _isNightVisual;
    private DateTime _earliestLoadedDate = DateTime.Today;

    public JuyaNewsWidget()
    {
        InitializeComponent();

        BrandTextBlock.FontFamily = MiSansFontFamily;
        LoadingTextBlock.FontFamily = MiSansFontFamily;
        StatusTextBlock.FontFamily = MiSansFontFamily;

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ApplyCellSize(_currentCellSize);
        ApplyLoadingState();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        UpdateAdaptiveLayout();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        _ = LoadInitialNewsAsync();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _isNightVisual = ResolveNightMode();
        UpdateAdaptiveLayout();
    }

    private bool ResolveNightMode()
    {
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }

        if (ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }

        if (this.TryFindResource("AdaptiveSurfaceBaseBrush", out var value) &&
            value is ISolidColorBrush brush)
        {
            return CalculateRelativeLuminance(brush.Color) < 0.45;
        }

        return true;
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var r = ToLinear(color.R / 255d);
        var g = ToLinear(color.G / 255d);
        var b = ToLinear(color.B / 255d);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private void ApplyNightModeVisual()
    {
        // 卡片背景
        CardBorder.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#2d2a2a") : Color.Parse("#fefefe"));
        
        // 品牌标题
        BrandTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#d4736a") : Color.Parse("#bb5649"));
        
        // 刷新按钮
        RefreshButton.BorderBrush = new SolidColorBrush(_isNightVisual ? Color.Parse("#d4736a") : Color.Parse("#bb5649"));
        RefreshButton.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#d4736a") : Color.Parse("#bb5649"));
        
        // 头像背景
        AvatarBorder.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#3d3a3a") : Color.Parse("#f8f5ec"));
        
        // 状态文字
        StatusTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#9a9590") : Color.Parse("#757575"));
        LoadingTextBlock.Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#9a9590") : Color.Parse("#757575"));

        // 更新所有日期视图的样式
        foreach (var view in _dailyViews)
        {
            view.ApplyNightMode(_isNightVisual);
        }
    }

    private async Task LoadInitialNewsAsync()
    {
        if (!_isAttached || _isLoading)
        {
            return;
        }

        _isLoading = true;
        LoadingTextBlock.IsVisible = true;
        StatusTextBlock.IsVisible = false;

        try
        {
            // 解析RSS获取所有新闻
            var allNews = await FetchJuyaNewsAsync();
            
            if (!_isAttached)
            {
                return;
            }

            // 缓存新闻数据
            foreach (var news in allNews)
            {
                _cachedNews[news.Date.Date] = news;
            }

            // 加载最近几天的新闻
            var today = DateTime.Today;
            var datesToLoad = Enumerable.Range(0, InitialLoadDays)
                .Select(i => today.AddDays(-i))
                .Where(d => _cachedNews.ContainsKey(d))
                .OrderByDescending(d => d)
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_isAttached) return;

                NewsStackPanel.Children.Clear();
                _dailyViews.Clear();
                _loadedDates.Clear();

                foreach (var date in datesToLoad)
                {
                    AddDailyNewsToView(_cachedNews[date]);
                    _loadedDates.Add(date);
                }

                if (_loadedDates.Any())
                {
                    _earliestLoadedDate = _loadedDates.Min();
                }

                LoadingTextBlock.IsVisible = false;
                StatusTextBlock.IsVisible = false;
                UpdateAdaptiveLayout();
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_isAttached) return;
                StatusTextBlock.Text = "加载失败";
                StatusTextBlock.IsVisible = true;
                LoadingTextBlock.IsVisible = false;
            });
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task<List<JuyaDailyNews>> FetchJuyaNewsAsync()
    {
        var result = new List<JuyaDailyNews>();
        
        try
        {
            // 使用字节数组获取内容，确保正确解码 UTF-8
            var response = await HttpClient.GetByteArrayAsync(RssUrl);
            var rssContent = System.Text.Encoding.UTF8.GetString(response);
            var doc = XDocument.Parse(rssContent);
            
            var contentNs = XNamespace.Get("http://purl.org/rss/1.0/modules/content/");
            
            var items = doc.Descendants("item");
            
            foreach (var item in items)
            {
                var title = item.Element("title")?.Value ?? "";
                var link = item.Element("link")?.Value ?? "";
                var pubDate = item.Element("pubDate")?.Value ?? "";
                var contentEncoded = item.Element(contentNs + "encoded")?.Value ?? "";
                
                // 解析日期
                if (!DateTime.TryParse(pubDate, out var date))
                {
                    date = DateTime.Today;
                }
                
                // 提取封面图URL
                var coverImageUrl = ExtractCoverImageUrl(contentEncoded);
                
                // 提取视频链接
                var (bilibiliUrl, youtubeUrl) = ExtractVideoUrls(contentEncoded);
                
                // 解析概览（简短列表）
                var overviewCategories = ParseOverview(contentEncoded);
                
                // 解析详细内容
                var detailedNews = ParseDetailedNews(contentEncoded);
                
                var news = new JuyaDailyNews(
                    Date: date,
                    Title: title,
                    CoverImageUrl: coverImageUrl,
                    IssueUrl: link,
                    BilibiliUrl: bilibiliUrl,
                    YoutubeUrl: youtubeUrl,
                    OverviewCategories: overviewCategories,
                    DetailedNews: detailedNews,
                    FetchedAt: DateTimeOffset.Now
                );
                
                result.Add(news);
            }
        }
        catch
        {
            // 返回空列表
        }
        
        return result.OrderByDescending(n => n.Date).ToList();
    }

    private static string ExtractCoverImageUrl(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "";
        }

        var match = Regex.Match(content, @"<img[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }

    private static (string bilibili, string youtube) ExtractVideoUrls(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ("", "");
        }

        string bilibiliUrl = "";
        string youtubeUrl = "";

        var bilibiliMatch = Regex.Match(content, @"<a[^>]+href=[""'](https?://(?:www\.)?bilibili\.com/[^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
        if (bilibiliMatch.Success)
        {
            bilibiliUrl = bilibiliMatch.Groups[1].Value;
        }

        var youtubeMatch = Regex.Match(content, @"<a[^>]+href=[""'](https?://(?:www\.)?(?:youtube\.com|youtu\.be)/[^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
        if (youtubeMatch.Success)
        {
            youtubeUrl = youtubeMatch.Groups[1].Value;
        }

        return (bilibiliUrl, youtubeUrl);
    }

    private static List<JuyaOverviewCategory> ParseOverview(string content)
    {
        var categories = new List<JuyaOverviewCategory>();
        
        if (string.IsNullOrWhiteSpace(content))
        {
            return categories;
        }

        var categoryIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["要闻"] = "📌",
            ["开发生态"] = "💻",
            ["产品应用"] = "📱",
            ["产品发布"] = "🚀",
            ["模型发布"] = "🤖",
            ["行业动态"] = "📈",
            ["技术与洞察"] = "🔍",
            ["学术研究"] = "📚",
            ["研究"] = "🔬",
            ["开源"] = "🔓",
            ["投资"] = "💰",
            ["融资"] = "💵",
            ["商业"] = "💼",
            ["市场"] = "📊",
            ["AI绘画"] = "🎨",
            ["设计"] = "✏️",
            ["创意"] = "💡",
            ["前瞻与传闻"] = "🔮",
            ["趋势"] = "📉",
            ["预测"] = "🔭",
            ["政策"] = "📋",
            ["法规"] = "⚖️",
            ["监管"] = "🛡️",
            ["硬件"] = "🔧",
            ["芯片"] = "🖥️",
            ["基础设施"] = "🏗️",
            ["其他"] = "•",
            ["要点"] = "📋",
            ["摘要"] = "📝"
        };

        var overviewMatch = Regex.Match(content, @"<h2>\s*概览\s*</h2>(.*?)(?:<hr>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        if (!overviewMatch.Success)
        {
            return categories;
        }

        var overviewContent = overviewMatch.Groups[1].Value;

        var h3Matches = Regex.Matches(overviewContent, @"<h3>([^<]+)</h3>\s*<ul>(.*?)</ul>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        foreach (Match match in h3Matches)
        {
            var categoryName = match.Groups[1].Value.Trim();
            var listContent = match.Groups[2].Value;
            
            var icon = categoryIcons.GetValueOrDefault(categoryName, "•");
            
            var items = new List<JuyaOverviewItem>();
            var itemMatches = Regex.Matches(listContent, @"<li>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            foreach (Match itemMatch in itemMatches)
            {
                var itemText = itemMatch.Groups[1].Value;
                
                string itemTitle;
                string itemUrl;
                int? number = null;
                
                var linkMatch = Regex.Match(itemText, @"<a[^>]+href=[""']([^""']+)[""'][^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                
                if (linkMatch.Success)
                {
                    itemUrl = linkMatch.Groups[1].Value;
                    var linkText = Regex.Replace(linkMatch.Groups[2].Value, @"<[^>]+>", "").Trim();
                    
                    var beforeLink = itemText.Substring(0, itemText.IndexOf("<a", StringComparison.OrdinalIgnoreCase));
                    itemTitle = Regex.Replace(beforeLink, @"<[^>]+>", "").Trim();
                    
                    if (string.IsNullOrWhiteSpace(itemTitle))
                    {
                        itemTitle = linkText;
                    }
                }
                else
                {
                    itemTitle = Regex.Replace(itemText, @"<[^>]+>", "").Trim();
                    itemUrl = "";
                }
                
                var numberMatch = Regex.Match(itemText, @"<code>\s*#(\d+)\s*</code>|#(\d+)");
                if (numberMatch.Success)
                {
                    number = int.Parse(numberMatch.Groups[1].Success ? numberMatch.Groups[1].Value : numberMatch.Groups[2].Value);
                }
                
                itemTitle = Regex.Replace(itemTitle, @"^\s*#\d+\s*", "").Trim();
                itemTitle = Regex.Replace(itemTitle, @"[→↗\s]+$", "").Trim();
                
                if (!string.IsNullOrWhiteSpace(itemTitle) && itemTitle.Length > 1)
                {
                    items.Add(new JuyaOverviewItem(itemTitle, itemUrl, number));
                }
            }
            
            if (items.Any())
            {
                categories.Add(new JuyaOverviewCategory(categoryName, icon, items));
            }
        }

        return categories;
    }

    private static List<JuyaDetailedNewsItem> ParseDetailedNews(string content)
    {
        var newsItems = new List<JuyaDetailedNewsItem>();
        
        if (string.IsNullOrWhiteSpace(content))
        {
            return newsItems;
        }

        var detailedMatch = Regex.Match(content, @"<hr>(.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!detailedMatch.Success)
        {
            return newsItems;
        }

        var detailedContent = detailedMatch.Groups[1].Value;

        var newsMatches = Regex.Matches(detailedContent, @"<h2>(.*?)</h2>(.*?)(?=<h2>|<hr>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        foreach (Match match in newsMatches)
        {
            var headerContent = match.Groups[1].Value;
            var bodyContent = match.Groups[2].Value;
            
            var numberMatch = Regex.Match(headerContent, @"<code>\s*#(\d+)\s*</code>");
            if (!numberMatch.Success)
            {
                numberMatch = Regex.Match(headerContent, @"#(\d+)");
            }
            
            int? number = numberMatch.Success ? int.Parse(numberMatch.Groups[1].Value) : null;
            
            string title;
            var linkMatch = Regex.Match(headerContent, @"<a[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (linkMatch.Success)
            {
                title = Regex.Replace(linkMatch.Groups[1].Value, @"<[^>]+>", "").Trim();
            }
            else
            {
                title = Regex.Replace(headerContent, @"<code>.*?</code>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                title = Regex.Replace(title, @"<[^>]+>", "").Trim();
                title = Regex.Replace(title, @"#\d+", "").Trim();
            }
            
            var bodyText = ExtractBodyText(bodyContent);
            
            var relatedLinks = new List<string>();
            var linkMatches = Regex.Matches(bodyContent, @"<a[^>]+href=[""']([^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
            foreach (Match linkMatch2 in linkMatches)
            {
                var url = linkMatch2.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(url) && !relatedLinks.Contains(url))
                {
                    relatedLinks.Add(url);
                }
            }
            
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(bodyText))
            {
                newsItems.Add(new JuyaDetailedNewsItem(title, number ?? 0, bodyText, relatedLinks));
            }
        }

        return newsItems;
    }

    private static string ExtractBodyText(string htmlContent)
    {
        if (string.IsNullOrWhiteSpace(htmlContent))
        {
            return "";
        }

        // 提取 blockquote 内容
        var blockquoteMatch = Regex.Match(htmlContent, @"<blockquote>(.*?)</blockquote>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (blockquoteMatch.Success)
        {
            var text = blockquoteMatch.Groups[1].Value;
            // 移除 <p> 标签但保留内容
            text = Regex.Replace(text, @"<p>(.*?)</p>", "$1\n\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            // 移除其他 HTML 标签
            text = Regex.Replace(text, @"<[^>]+>", "");
            // 清理多余空白
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        // 如果没有 blockquote，提取所有 <p> 标签内容
        var paragraphs = Regex.Matches(htmlContent, @"<p>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (paragraphs.Count > 0)
        {
            var text = string.Join("\n\n", paragraphs.Cast<Match>().Select(m => 
                Regex.Replace(m.Groups[1].Value, @"<[^>]+>", "").Trim()));
            return text.Trim();
        }

        // 最后尝试直接移除所有 HTML 标签
        return Regex.Replace(htmlContent, @"<[^>]+>", "").Trim();
    }

    private void AddDailyNewsToView(JuyaDailyNews news)
    {
        var view = new DailyNewsView(news, _isNightVisual);
        view.CoverImageClicked += (s, e) => TryOpenUrl(news.IssueUrl);
        view.NewsItemClicked += (s, url) => TryOpenUrl(url);
        NewsStackPanel.Children.Add(view);
        _dailyViews.Add(view);
    }

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isLoading || !_isAttached)
        {
            return;
        }

        var scrollViewer = (ScrollViewer)sender!;
        
        var offset = scrollViewer.Offset;
        var extent = scrollViewer.Extent;
        var viewport = scrollViewer.Viewport;
        
        if (offset.Y >= extent.Height - viewport.Height - 200)
        {
            await LoadMoreNewsAsync();
        }
    }

    private async Task LoadMoreNewsAsync()
    {
        if (_isLoading || !_isAttached)
        {
            return;
        }

        var nextDates = Enumerable.Range(1, LoadMoreDays)
            .Select(i => _earliestLoadedDate.AddDays(-i))
            .Where(d => _cachedNews.ContainsKey(d) && !_loadedDates.Contains(d))
            .ToList();

        if (!nextDates.Any())
        {
            return;
        }

        _isLoading = true;
        LoadingTextBlock.IsVisible = true;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_isAttached) return;

                foreach (var date in nextDates.OrderByDescending(d => d))
                {
                    AddDailyNewsToView(_cachedNews[date]);
                    _loadedDates.Add(date);
                }

                _earliestLoadedDate = _loadedDates.Min();
                LoadingTextBlock.IsVisible = false;
                UpdateAdaptiveLayout();
            });
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async void OnRefreshButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        RefreshButtonText.Text = "刷新中...";
        RefreshIcon.IsEnabled = false;

        try
        {
            var allNews = await FetchJuyaNewsAsync();
            
            if (!_isAttached)
            {
                return;
            }

            var today = DateTime.Today;
            var todayNews = allNews.FirstOrDefault(n => n.Date.Date == today);
            
            if (todayNews != null)
            {
                _cachedNews[today] = todayNews;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_isAttached) return;

                    var existingIndex = _loadedDates.IndexOf(today);
                    if (existingIndex >= 0 && _dailyViews.Count > existingIndex)
                    {
                        var oldView = _dailyViews[existingIndex];
                        var insertIndex = NewsStackPanel.Children.IndexOf(oldView);
                        
                        if (insertIndex >= 0)
                        {
                            NewsStackPanel.Children.RemoveAt(insertIndex);
                            _dailyViews.RemoveAt(existingIndex);
                            
                            var newView = new DailyNewsView(todayNews, _isNightVisual);
                            newView.CoverImageClicked += (s, e) => TryOpenUrl(todayNews.IssueUrl);
                            
                            NewsStackPanel.Children.Insert(insertIndex, newView);
                            _dailyViews.Insert(existingIndex, newView);
                        }
                    }
                    else
                    {
                        var newView = new DailyNewsView(todayNews, _isNightVisual);
                        newView.CoverImageClicked += (s, e) => TryOpenUrl(todayNews.IssueUrl);
                        
                        NewsStackPanel.Children.Insert(0, newView);
                        _dailyViews.Insert(0, newView);
                        _loadedDates.Insert(0, today);
                    }

                    RefreshButtonText.Text = "刷新";
                    RefreshIcon.IsEnabled = true;
                    UpdateAdaptiveLayout();
                });
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RefreshButtonText.Text = "刷新";
                    RefreshIcon.IsEnabled = true;
                });
            }
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshButtonText.Text = "刷新";
                RefreshIcon.IsEnabled = true;
            });
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void TryOpenUrl(string? url)
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
            // 忽略错误
        }
    }

    private void ApplyLoadingState()
    {
        StatusTextBlock.Text = "加载中...";
        StatusTextBlock.IsVisible = true;
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = ResolveScale();
        var softScale = Math.Clamp(scale, 0.80, 1.32);
        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        var unifiedMainRectangle = ResolveUnifiedMainRectangle();
        RootBorder.CornerRadius = unifiedMainRectangle;
        CardBorder.CornerRadius = unifiedMainRectangle;

        var horizontalPadding = Math.Clamp(16 * softScale, 10, 24);
        var verticalPadding = Math.Clamp(14 * softScale, 8, 20);
        CardBorder.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);

        var headerHeight = Math.Clamp(40 * softScale, 28, 56);
        HeaderGrid.Height = headerHeight;

        BrandTextBlock.FontSize = Math.Clamp(20 * softScale, 14, 26);

        var avatarSize = Math.Clamp(36 * softScale, 24, 48);
        AvatarBorder.Width = avatarSize;
        AvatarBorder.Height = avatarSize;
        AvatarBorder.CornerRadius = new CornerRadius(avatarSize / 2);

        var buttonFontSize = Math.Clamp(13 * softScale, 10, 16);
        RefreshButton.FontSize = buttonFontSize;
        RefreshButton.Padding = new Thickness(
            Math.Clamp(8 * softScale, 6, 12),
            Math.Clamp(4 * softScale, 2, 6)
        );

        StatusTextBlock.FontSize = Math.Clamp(16 * softScale, 12, 22);
        LoadingTextBlock.FontSize = Math.Clamp(14 * softScale, 11, 18);

        foreach (var view in _dailyViews)
        {
            view.UpdateLayout(softScale, totalWidth - horizontalPadding * 2);
        }

        ApplyNightModeVisual();
    }

    private double ResolveScale()
    {
        var expectedWidth = _currentCellSize * BaseWidthCells;
        var expectedHeight = _currentCellSize * BaseHeightCells;
        if (expectedWidth <= 0 || expectedHeight <= 0)
        {
            return 1d;
        }

        var actualWidth = Bounds.Width > 1 ? Bounds.Width : expectedWidth;
        var actualHeight = Bounds.Height > 1 ? Bounds.Height : expectedHeight;
        var scaleX = actualWidth / expectedWidth;
        var scaleY = actualHeight / expectedHeight;
        return Math.Clamp(Math.Min(scaleX, scaleY), 0.72, 2.4);
    }

    private CornerRadius ResolveUnifiedMainRectangle() => new(ResolveUnifiedMainRadiusValue());

    private static double ResolveUnifiedMainRadiusValue() =>
        HostAppearanceThemeProvider.GetOrCreate().GetCurrent().CornerRadiusTokens.Lg.TopLeft;
}

// 数据模型
public sealed record JuyaDailyNews(
    DateTime Date,
    string Title,
    string CoverImageUrl,
    string IssueUrl,
    string BilibiliUrl,
    string YoutubeUrl,
    IReadOnlyList<JuyaOverviewCategory> OverviewCategories,
    IReadOnlyList<JuyaDetailedNewsItem> DetailedNews,
    DateTimeOffset FetchedAt);

public sealed record JuyaOverviewCategory(
    string Name,
    string Icon,
    IReadOnlyList<JuyaOverviewItem> Items);

public sealed record JuyaOverviewItem(
    string Title,
    string Url,
    int? Number);

public sealed record JuyaDetailedNewsItem(
    string Title,
    int Number,
    string BodyText,
    IReadOnlyList<string> RelatedLinks);
