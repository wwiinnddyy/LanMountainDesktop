# 橘鸦新闻组件 UI 设计文档

## 1. 数据源分析

### RSS 结构
```xml
<item>
  <title>2026-03-23</title>                    <!-- 日期作为标题 -->
  <link>https://imjuya.github.io/juya-ai-daily/issue-37/</link>
  <description>AI 早报 2026-03-23 视频版...</description>
  <content:encoded>
    <![CDATA[
      <img src="封面图片URL" alt="">           <!-- 每日封面图 -->
      <h1>AI 早报 2026-03-23</h1>
      <p><strong>视频版</strong>: B站链接 | YouTube链接</p>
      <h2>要闻</h2>
      <ul>
        <li>微信正式推出ClawBot插件... #1</li>
      </ul>
      <h2>开发者</h2>
      <ul>
        <li>Claude Code 测试新功能... #2</li>
      </ul>
      ...更多分类
    ]]>
  </content:encoded>
  <pubDate>Mon, 23 Mar 2026 00:34:38 +0000</pubDate>
</item>
```

### 推送时间规律
- **推送时间**: 每天凌晨 00:30 - 02:00 (UTC+0)
- **北京时间**: 每天上午 08:30 - 10:00
- **历史数据**: RSS包含约30天的历史数据(从2026-02-18开始)
- **更新频率**: 每日一期，一期多条新闻

### 内容结构
每期早报包含：
1. **封面图片** - 每日独特的封面图
2. **视频版链接** - B站和YouTube双平台
3. **要闻** - 2-3条重要新闻
4. **开发者** - 技术相关动态
5. **产品发布** - 新产品/功能
6. **模型发布** - AI模型更新
7. **其他分类** - 投资、开源、研究等

---

## 2. 设计理念

### 品牌调性
- **橘鸦官网风格**: 柔和、温暖、阅读友好
- **主色调**: 砖红色/陶土色 (#bb5649) - 来自官网
- **背景色**: 米白色/奶油色 (#fefefe, #f8f5ec) - 柔和不刺眼
- **文字色**: 深灰蓝 (#34495e) - 温和专业
- **视觉风格**: 简洁优雅、阅读舒适、温暖亲切

### 设计关键词
- 柔和温暖
- 阅读友好
- 优雅简洁
- 舒适护眼
- **垂直连续滚动** ← 核心交互

---

## 3. 色彩方案 (参考橘鸦官网)

### 官网色彩提取
```
官网主色 (砖红/陶土): #bb5649
官网文字: #34495e
官网背景: #fefefe
官网次要背景: #f8f5ec (米黄/奶油)
官网引用块背景: rgba(192,91,77,.05)
官网引用块边框: rgba(192,91,77,.3)
官网链接悬停: #bb5649
官网元信息: #757575
```

### 日间模式 (Light Mode) - 柔和风格
| 元素 | 颜色 | 用途 |
|-----|------|------|
| 卡片背景 | #fefefe | 主卡片底色 (官网背景色) |
| 卡片边框 | #e6e6e6 | 细微边框 |
| 品牌标题 | #bb5649 | "橘鸦" 文字 (官网主色) |
| 日期标题 | #bb5649 | 日期大标题 |
| 新闻标题 | #34495e | 新闻条目文字 |
| 分类标签 | #bb5649 | 要闻/开发者等 |
| 时间戳 | #757575 | 发布时间 |
| 悬停背景 | rgba(192,91,77,.05) | 条目悬停效果 |
| 分隔线 | #e6e6e6 | 日期分隔 |
| 加载提示 | #757575 | 加载更多提示 |

### 夜间模式 (Dark Mode) - 柔和暗色
| 元素 | 颜色 | 用途 |
|-----|------|------|
| 卡片背景 | #2d2a2a | 深暖灰 |
| 卡片边框 | #3d3a3a | 细微边框 |
| 品牌标题 | #d4736a | 柔和砖红 |
| 日期标题 | #d4736a | 日期大标题 |
| 新闻标题 | #e8e4e0 | 新闻条目文字 |
| 分类标签 | #d4736a | 要闻/开发者等 |
| 时间戳 | #9a9590 | 次要信息 |
| 悬停背景 | rgba(212,115,106,.1) | 条目悬停效果 |
| 分隔线 | #3d3a3a | 日期分隔 |
| 加载提示 | #9a9590 | 加载更多提示 |

---

## 4. 布局设计

### 组件尺寸
- **默认尺寸**: 4格宽 x 4格高
- **最小尺寸**: 4格宽 x 4格高
- **滚动方向**: 垂直滚动

### 垂直连续滚动布局

```
┌─────────────────────────────────────────┐
│  🧱 橘鸦 · AI早报          [🔗 官网]     │  ← Header (固定或随滚动)
├─────────────────────────────────────────┤
│                                         │
│  ┌───────────────────────────────────┐  │
│  │      📰 封面图 2026-03-23         │  │  ← 今天的新闻
│  │                                   │  │
│  └───────────────────────────────────┘  │
│                                         │
│  # 2026年3月23日 星期一                 │  ← 日期大标题
│                                         │
│  ## 📌 要闻                             │
│  • 微信正式推出ClawBot插件...          │
│  • OpenAI发布GPT-5.4预览版...          │
│                                         │
│  ## 💻 开发者                           │
│  • Claude Code测试新功能...            │
│  • 阶跃星辰推出StepPlan...             │
│                                         │
│  📺 视频版: B站 | YouTube              │
│                                         │
│  ─────────────────────────────────────  │  ← 日期分隔线
│                                         │
│  ┌───────────────────────────────────┐  │
│  │      📰 封面图 2026-03-22         │  │  ← 昨天的新闻
│  │                                   │  │     (往下滑动显示)
│  └───────────────────────────────────┘  │
│                                         │
│  # 2026年3月22日 星期日                 │
│                                         │
│  ## 📌 要闻                             │
│  • OpenAI发布GPT-5.4...                │
│  • Google推出新功能...                 │
│                                         │
│  ## 💻 开发者                           │
│  • Anthropic更新Claude...              │
│                                         │
│  📺 视频版: B站 | YouTube              │
│                                         │
│  ─────────────────────────────────────  │
│                                         │
│  ┌───────────────────────────────────┐  │  ← 前天的新闻
│  │      📰 封面图 2026-03-21         │  │     (继续往下滑动)
│  │                                   │  │
│  └───────────────────────────────────┘  │
│                                         │
│  # 2026年3月21日 星期六                 │
│                                         │
│  ...                                    │
│                                         │
│  ─────────────────────────────────────  │
│                                         │
│         正在加载更多... ↓               │  ← 加载提示
│                                         │
└─────────────────────────────────────────┘
```

### 日期分隔设计
```
┌─────────────────────────────────────────┐
│                                         │
│  ─────────── 3月22日 星期日 ───────────  │  ← 日期分隔条
│                                         │
│  [昨天的新闻内容]                        │
│                                         │
└─────────────────────────────────────────┘
```

### 单期新闻结构
```
┌─────────────────────────────────────────┐
│                                         │
│  [封面图 - 16:9 比例]                    │
│                                         │
│  # 2026年3月23日 星期一                  │  ← 日期大标题
│                                         │
│  ## 📌 要闻                             │  ← 分类标题
│  • 新闻条目1                            │
│  • 新闻条目2                            │
│                                         │
│  ## 💻 开发者                           │
│  • 新闻条目3                            │
│  • 新闻条目4                            │
│                                         │
│  ## 🚀 产品发布                         │
│  • 新闻条目5                            │
│                                         │
│  📺 视频版: [B站] [YouTube]            │  ← 视频链接
│                                         │
└─────────────────────────────────────────┘
```

---

## 5. 字体规范

### 字体族
```xml
FontFamily="MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans"
```

### 字号规范

| 元素 | 字号 | 字重 | 说明 |
|-----|------|------|------|
| 品牌标题 | 20px | SemiBold | 顶部固定标题 |
| 日期大标题 | 22px | Bold | 每期日期 |
| 分类标题 | 16px | SemiBold | 要闻/开发者等 |
| 新闻条目 | 14px | Regular | 主要阅读内容 |
| 视频链接 | 13px | Regular | 底部视频入口 |
| 加载提示 | 13px | Regular | 加载更多 |

---

## 6. 核心交互: 垂直连续滚动

### 滚动行为
```
用户往下滑动
    ↓
显示今天的新闻内容
    ↓
继续往下滑动
    ↓
显示日期分隔线
    ↓
显示昨天的新闻内容
    ↓
继续往下滑动
    ↓
显示前天的新闻内容
    ↓
...
    ↓
到达已加载内容的底部
    ↓
显示"正在加载更多..."
    ↓
自动加载更早的新闻
```

### 加载策略
```csharp
// 初始加载: 最近3天的新闻
// 滚动到底部: 自动加载接下来3天
// 最大加载: 30天历史数据
// 内存管理: 只保留可视区域 ±3 天的数据
```

### 滚动位置记忆
```csharp
// 记录用户当前滚动位置
// 切换主题/刷新时不重置位置
// 下次打开组件时恢复到上次位置
```

---

## 7. 交互设计

### 悬停效果
```
新闻条目悬停:
- 背景色: 透明 → rgba(192,91,77,.05)
- 过渡时间: 200ms
- 光标: Hand cursor
```

### 点击效果
```
新闻条目点击:
- 打开浏览器跳转原文链接
- 轻微缩放: scale(0.98)
- 过渡时间: 100ms
```

### 封面图点击
```
封面图点击:
- 打开当期官网页面
- 轻微放大效果
```

### 日期标题点击
```
日期标题点击:
- 展开/收起该期新闻
- 箭头图标旋转动画
```

---

## 8. 动画效果

### 滚动动画
```
内容跟随滚动:
- 自然滚动，无额外动画
- 保持流畅 60fps
```

### 加载动画
```
新内容加载:
- 淡入: opacity 0 → 1 (300ms)
- 缓动: ease-out
```

### 日期分隔线动画
```
日期分隔线进入视口:
- 轻微放大: scale(0.95) → scale(1)
- 透明度: 0.5 → 1
- 时长: 200ms
```

---

## 9. 响应式适配

### 缩放规则
```csharp
scale = Math.Clamp(currentCellSize / 48, 0.56, 2.0)

字体缩放: baseFontSize * scale
间距缩放: baseSpacing * scale
```

### 最小尺寸保障
```
最小字体: 11px
最小间距: 8px
最小触摸区域: 44px
```

---

## 10. 代码结构预览

### XAML 结构
```xml
<UserControl>
    <Border x:Name="RootBorder" CornerRadius="24" Background="#fefefe">
        <Grid RowDefinitions="Auto,*">
            
            <!-- Header (固定) -->
            <Grid Grid.Row="0" ColumnDefinitions="*,Auto" Margin="16">
                <TextBlock Text="🧱 橘鸦 · AI早报" 
                           Foreground="#bb5649" FontSize="20"/>
                <Button x:Name="OfficialWebsiteButton" Grid.Column="1"
                        Content="🔗 官网" Click="OnOfficialWebsiteClick"
                        Background="Transparent" Foreground="#bb5649"/>
            </Grid>
            
            <!-- 滚动内容区 -->
            <ScrollViewer Grid.Row="1" x:Name="ContentScrollViewer"
                          VerticalScrollBarVisibility="Auto">
                <StackPanel x:Name="NewsStackPanel">
                    
                    <!-- 今天的新闻 -->
                    <local:DailyNewsView Date="2026-03-23" 
                                         CoverImageUrl="..."
                                         Categories="..."/>
                    
                    <!-- 日期分隔线 -->
                    <local:DateSeparator Date="2026-03-22"/>
                    
                    <!-- 昨天的新闻 -->
                    <local:DailyNewsView Date="2026-03-22"
                                         CoverImageUrl="..."
                                         Categories="..."/>
                    
                    <!-- 更多历史新闻... -->
                    
                    <!-- 加载提示 -->
                    <TextBlock x:Name="LoadingMoreText" 
                               Text="正在加载更多... ↓"
                               HorizontalAlignment="Center"
                               Margin="0,20"/>
                    
                </StackPanel>
            </ScrollViewer>
            
        </Grid>
    </Border>
</UserControl>
```

### DailyNewsView 组件
```xml
<!-- 单期新闻视图 -->
<Border x:Class="DailyNewsView" Margin="0,0,0,24">
    <StackPanel>
        <!-- 封面图 -->
        <Border CornerRadius="12" ClipToBounds="True"
                PointerPressed="OnCoverImageClick" Cursor="Hand">
            <Image Source="{Binding CoverImageUrl}" Stretch="UniformToFill"/>
        </Border>
        
        <!-- 日期大标题 -->
        <TextBlock Text="{Binding FormattedDate}" 
                   FontSize="22" FontWeight="Bold"
                   Foreground="#bb5649" Margin="0,16,0,12"/>
        
        <!-- 分类列表 -->
        <ItemsControl ItemsSource="{Binding Categories}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <StackPanel Margin="0,0,0,12">
                        <TextBlock Text="{Binding IconAndName}" 
                                   FontSize="16" FontWeight="SemiBold"
                                   Foreground="#bb5649"/>
                        <ItemsControl ItemsSource="{Binding Items}"/>
                    </StackPanel>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        
        <!-- 视频链接 -->
        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
            <TextBlock Text="📺 视频版:" Foreground="#757575"/>
            <HyperlinkButton Content="B站" NavigateUri="{Binding BilibiliUrl}"/>
            <TextBlock Text="|" Foreground="#757575" Margin="4,0"/>
            <HyperlinkButton Content="YouTube" NavigateUri="{Binding YoutubeUrl}"/>
        </StackPanel>
        
    </StackPanel>
</Border>
```

---

## 11. 数据模型

```csharp
// 每日早报数据
public sealed record JuyaDailyNews(
    DateTime Date,
    string Title,
    string CoverImageUrl,
    string IssueUrl,
    string BilibiliUrl,
    string YoutubeUrl,
    IReadOnlyList<JuyaNewsCategory> Categories,
    DateTimeOffset FetchedAt);

// 新闻分类
public sealed record JuyaNewsCategory(
    string Name,
    string Icon,
    IReadOnlyList<JuyaNewsItem> Items);

// 单条新闻
public sealed record JuyaNewsItem(
    string Title,
    string Url,
    int? Number);
```

---

## 12. 与现有组件对比

| 特性 | CnrDailyNews | IfengNews | **JuyaNews (建议)** |
|-----|--------------|-----------|---------------------|
| 浏览方式 | 静态展示 | 静态展示 | **垂直连续滚动** |
| 历史查看 | 不支持 | 不支持 | **下滑自动加载** |
| 交互方式 | 点击刷新 | 点击刷新 | **滚动浏览** |
| 内容组织 | 平铺 | 平铺 | **按日期分组** |

---

## 13. 设计亮点

1. **垂直滚动**: 像社交媒体一样自然浏览
2. **连续阅读**: 今天→昨天→前天，无缝衔接
3. **日期分隔**: 清晰的日期标识，不会混淆
4. **自动加载**: 滑到底部自动加载更多历史
5. **柔和色彩**: 砖红色 + 米白色，阅读舒适
6. **主题适配**: 日间/夜间模式都柔和护眼

---

## 14. 实现建议

### 滚动加载实现
```csharp
public partial class JuyaNewsWidget : UserControl
{
    private readonly List<JuyaDailyNews> _loadedNews = new();
    private DateTime _earliestLoadedDate;
    private bool _isLoadingMore;
    
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = (ScrollViewer)sender!;
        
        // 检测是否滚动到底部
        if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 100)
        {
            LoadMoreNews();
        }
    }
    
    private async void LoadMoreNews()
    {
        if (_isLoadingMore) return;
        _isLoadingMore = true;
        
        // 加载接下来3天的新闻
        var nextBatch = await FetchNewsBatch(_earliestLoadedDate.AddDays(-1), 3);
        
        foreach (var news in nextBatch)
        {
            AddNewsToView(news);
            _loadedNews.Add(news);
        }
        
        _earliestLoadedDate = nextBatch.Last().Date;
        _isLoadingMore = false;
    }
}
```

### 内存优化
```csharp
// 只保留可视区域附近的新闻
// 远离可视区域的新闻释放图片资源
// 保留文字内容，图片按需加载
```

---

*设计版本: v4.0*
*更新日期: 2026-03-24*
*更新内容: 改为垂直连续滚动浏览模式*
