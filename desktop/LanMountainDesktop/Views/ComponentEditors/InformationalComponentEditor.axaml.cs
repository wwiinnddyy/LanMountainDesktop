using LanMountainDesktop.ComponentSystem;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class InformationalComponentEditor : ComponentEditorViewBase
{
    private readonly string _description;

    public InformationalComponentEditor()
        : this(null, "This component currently exposes instance information only.")
    {
    }

    public InformationalComponentEditor(DesktopComponentEditorContext? context, string description)
        : base(context)
    {
        _description = description;
        InitializeComponent();
        ApplyState();
    }

    private void ApplyState()
    {
        ComponentLabelTextBlock.Text = L("component.editor.id_label", "Component");
        ComponentValueTextBlock.Text = Context?.ComponentId ?? "-";
        PlacementLabelTextBlock.Text = L("component.editor.placement_label", "Placement");
        PlacementValueTextBlock.Text = Context?.PlacementId ?? "-";
        ScopeLabelTextBlock.Text = L("component.editor.scope_label", "Scope");
        ScopeValueTextBlock.Text = L("component.editor.scope_instance", "Instance-scoped editor");
    }
}
