using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LanMountainDesktop.Mobile.ViewModels;

/// <summary>
/// 组件面板视图模型。
/// 移动端不提供桌面自由摆放，组件以卡片流形式呈现。
/// 后续由组件运行时（DesktopComponents.Runtime）通过
/// <see cref="RegisterWidget"/> 注入真实组件视图。
/// </summary>
public sealed partial class WidgetPanelViewModel : ObservableObject
{
    public ObservableCollection<WidgetCardViewModel> Widgets { get; } = [];

    [ObservableProperty]
    private string _title = "组件面板";

    public WidgetPanelViewModel()
    {
        // 首版占位卡片：标记组件运行时的接入点。
        Widgets.Add(new WidgetCardViewModel("时间表", "课程表 / 日程组件将在此呈现"));
        Widgets.Add(new WidgetCardViewModel("天气", "天气组件将在此呈现"));
        Widgets.Add(new WidgetCardViewModel("插件组件", "已安装插件提供的组件将在此呈现"));
    }

    /// <summary>
    /// 组件运行时接入点：注册一个真实组件卡片。
    /// </summary>
    public void RegisterWidget(WidgetCardViewModel widget)
    {
        Widgets.Add(widget);
    }
}

/// <summary>
/// 单个组件卡片。<see cref="Content"/> 为空时显示占位说明文本，
/// 非空时承载组件运行时提供的真实控件。
/// </summary>
public sealed partial class WidgetCardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _placeholder;

    [ObservableProperty]
    private object? _content;

    public WidgetCardViewModel(string name, string placeholder, object? content = null)
    {
        _name = name;
        _placeholder = placeholder;
        _content = content;
    }
}
