using System.Globalization;

using LanMountainDesktop.ComponentSystem;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class InformationalComponentEditor : ComponentEditorViewBase
{
    public InformationalComponentEditor()
        : this(null)
    {
    }

    public InformationalComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
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

        var displayName = Context?.Definition.DisplayName;
        DescriptionTextBlock.Text = string.Format(
            CultureInfo.CurrentCulture,
            L(
                "component.editor.instance_only_hint",
                "This {0} component currently exposes instance-scoped editor metadata only."),
            string.IsNullOrWhiteSpace(displayName) ? "desktop" : displayName);
    }
}
