namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Options for registering an AirApp desktop component.
/// </summary>
public sealed class AirAppComponentOptions
{
    /// <summary>
    /// Gets or sets the unique component identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the widget implementation type.
    /// Must implement IAirAppWidget.
    /// </summary>
    public required Type WidgetType { get; set; }

    /// <summary>
    /// Gets or sets the optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the default width in grid cells.
    /// Default is 2.
    /// </summary>
    public int DefaultWidth { get; set; } = 2;

    /// <summary>
    /// Gets or sets the default height in grid cells.
    /// Default is 2.
    /// </summary>
    public int DefaultHeight { get; set; } = 2;

    /// <summary>
    /// Gets or sets the resize mode.
    /// </summary>
    public AirAppComponentResizeMode ResizeMode { get; set; } = AirAppComponentResizeMode.Both;

    /// <summary>
    /// Gets or sets whether this component can be added multiple times.
    /// Default is true.
    /// </summary>
    public bool AllowMultipleInstances { get; set; } = true;

    /// <summary>
    /// Gets or sets the category for grouping in the component library.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets the icon identifier.
    /// </summary>
    public string? IconKey { get; set; }
}
