using Avalonia;
using Avalonia.Controls;
using FluentIcons.Common;

namespace LanMountainDesktop.Controls;

public partial class SettingsSectionCard : UserControl
{
    public static readonly StyledProperty<string?> IconKeyProperty =
        AvaloniaProperty.Register<SettingsSectionCard, string?>(nameof(IconKey), "Settings");

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingsSectionCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsSectionCard, string?>(nameof(Description));

    public static readonly StyledProperty<object?> CardContentProperty =
        AvaloniaProperty.Register<SettingsSectionCard, object?>(nameof(CardContent));

    public SettingsSectionCard()
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

    public object? CardContent
    {
        get => GetValue(CardContentProperty);
        set => SetValue(CardContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconKeyProperty ||
            change.Property == TitleProperty ||
            change.Property == DescriptionProperty ||
            change.Property == CardContentProperty)
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
            CardContentHost is null)
        {
            return;
        }

        CardIcon.Symbol = MapIcon(IconKey);
        IconHost.IsVisible = !string.IsNullOrWhiteSpace(IconKey);

        TitleTextBlock.Text = Title ?? string.Empty;
        DescriptionTextBlock.Text = Description ?? string.Empty;
        DescriptionTextBlock.IsVisible = !string.IsNullOrWhiteSpace(Description);

        CardContentHost.Content = CardContent;
    }

    private static Symbol MapIcon(string? iconKey)
    {
        return iconKey?.Trim() switch
        {
            "DesignIdeas" => Symbol.Color,
            "Image" => Symbol.Image,
            "GridDots" => Symbol.GridDots,
            "PuzzlePiece" => Symbol.PuzzlePiece,
            "Info" => Symbol.Info,
            "ArrowSync" => Symbol.ArrowSync,
            _ => Symbol.Settings
        };
    }
}
