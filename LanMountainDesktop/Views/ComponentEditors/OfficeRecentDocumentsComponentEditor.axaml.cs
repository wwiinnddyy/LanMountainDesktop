using System;
using System.Linq;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class OfficeRecentDocumentsComponentEditor : ComponentEditorViewBase
{
    private bool _suppressEvents;

    public OfficeRecentDocumentsComponentEditor()
        : this(null)
    {
    }

    public OfficeRecentDocumentsComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        ApplyState();
    }

    private void ApplyState()
    {
        var snapshot = LoadSnapshot();
        var enabledSources = OfficeRecentDocumentSourceTypes.NormalizeValues(
            snapshot.OfficeRecentDocumentsEnabledSources,
            useDefaultWhenEmpty: snapshot.OfficeRecentDocumentsEnabledSources is null);

        HeadlineTextBlock.Text = Context?.Definition.DisplayName ?? "Office Recent Documents";
        DescriptionTextBlock.Text = L(
            "office_recent_documents.settings.desc",
            "Choose which Windows and Office sources this widget should scan for recent documents.");
        SourcesHeaderTextBlock.Text = L(
            "office_recent_documents.settings.sources_title",
            "Recent document sources");
        SourcesDescriptionTextBlock.Text = L(
            "office_recent_documents.settings.sources_desc",
            "You can combine multiple sources. Registry selection also keeps the Office interop MRU fallback available.");
        RegistryCheckBox.Content = L(
            "office_recent_documents.settings.source.registry",
            "Office registry MRU");
        RecentFoldersCheckBox.Content = L(
            "office_recent_documents.settings.source.recent_folders",
            "Windows Recent folders");
        JumpListsCheckBox.Content = L(
            "office_recent_documents.settings.source.jump_lists",
            "Windows Jump Lists");
        HintTextBlock.Text = L(
            "office_recent_documents.settings.hint",
            "If you disable all sources, this widget will stay empty until at least one source is enabled again.");

        _suppressEvents = true;
        RegistryCheckBox.IsChecked = enabledSources.Contains(OfficeRecentDocumentSourceTypes.Registry, StringComparer.OrdinalIgnoreCase);
        RecentFoldersCheckBox.IsChecked = enabledSources.Contains(OfficeRecentDocumentSourceTypes.RecentFolders, StringComparer.OrdinalIgnoreCase);
        JumpListsCheckBox.IsChecked = enabledSources.Contains(OfficeRecentDocumentSourceTypes.JumpLists, StringComparer.OrdinalIgnoreCase);
        _suppressEvents = false;
    }

    private void OnSourceSelectionChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var selectedSources = new[]
        {
            RegistryCheckBox.IsChecked == true ? OfficeRecentDocumentSourceTypes.Registry : null,
            RecentFoldersCheckBox.IsChecked == true ? OfficeRecentDocumentSourceTypes.RecentFolders : null,
            JumpListsCheckBox.IsChecked == true ? OfficeRecentDocumentSourceTypes.JumpLists : null
        }
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Cast<string>()
        .ToList();

        var snapshot = LoadSnapshot();
        snapshot.OfficeRecentDocumentsEnabledSources = selectedSources;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.OfficeRecentDocumentsEnabledSources));
    }
}
