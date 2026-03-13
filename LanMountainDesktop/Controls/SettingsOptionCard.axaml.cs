using Avalonia;
using Avalonia.Controls;
using FluentIcons.Common;

namespace LanMountainDesktop.Controls;

public partial class SettingsOptionCard : UserControl
{
    public static readonly StyledProperty<string?> IconKeyProperty =
        AvaloniaProperty.Register<SettingsOptionCard, string?>(nameof(IconKey), "Settings");

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingsOptionCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsOptionCard, string?>(nameof(Description));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<SettingsOptionCard, object?>(nameof(ActionContent));

    public static readonly StyledProperty<object?> DetailsContentProperty =
        AvaloniaProperty.Register<SettingsOptionCard, object?>(nameof(DetailsContent));

    public SettingsOptionCard()
    {
        InitializeComponent();
        RefreshVisualState();
    }

    public string? IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public object? DetailsContent
    {
        get => GetValue(DetailsContentProperty);
        set => SetValue(DetailsContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconKeyProperty ||
            change.Property == TitleProperty ||
            change.Property == DescriptionProperty ||
            change.Property == ActionContentProperty ||
            change.Property == DetailsContentProperty)
        {
            RefreshVisualState();
        }
    }

    private void RefreshVisualState()
    {
        if (CardIcon is null ||
            IconHost is null ||
            TitleTextBlock is null ||
            DescriptionTextBlock is null ||
            ActionContentHost is null ||
            DetailsContentHost is null)
        {
            return;
        }

        CardIcon.Symbol = MapIcon(IconKey);
        IconHost.IsVisible = !string.IsNullOrWhiteSpace(IconKey);

        TitleTextBlock.Text = Title ?? string.Empty;
        DescriptionTextBlock.Text = Description ?? string.Empty;
        DescriptionTextBlock.IsVisible = !string.IsNullOrWhiteSpace(Description);

        ActionContentHost.Content = ActionContent;
        ActionContentHost.IsVisible = ActionContent is not null;

        DetailsContentHost.Content = DetailsContent;
        DetailsContentHost.IsVisible = DetailsContent is not null;
    }

    private static Symbol MapIcon(string? iconKey)
    {
        return iconKey?.Trim() switch
        {
            "DesignIdeas" => Symbol.Color,
            "GridDots" => Symbol.GridDots,
            "PuzzlePiece" => Symbol.PuzzlePiece,
            "Info" => Symbol.Info,
            "ArrowSync" => Symbol.ArrowSync,
            _ => Symbol.Settings
        };
    }
}
