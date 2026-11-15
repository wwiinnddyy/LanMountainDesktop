using Avalonia.Controls;
using TKCode.Arc;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class NotificationBoxComponentEditor : ComponentEditorViewBase
{
    public NotificationBoxComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        DataContext = new NotificationBoxEditorViewModel(context);
    }
}
