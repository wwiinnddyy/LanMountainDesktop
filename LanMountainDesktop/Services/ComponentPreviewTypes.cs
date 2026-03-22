using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LanMountainDesktop.Services;

public enum ComponentPreviewKeyKind
{
    ComponentType = 0,
    PlacementInstance = 1
}

public readonly record struct ComponentPreviewKey
{
    private ComponentPreviewKey(
        ComponentPreviewKeyKind kind,
        string componentTypeId,
        string? placementId,
        int widthCells,
        int heightCells)
    {
        Kind = kind;
        ComponentTypeId = NormalizeRequired(componentTypeId, nameof(componentTypeId));
        PlacementId = kind == ComponentPreviewKeyKind.PlacementInstance
            ? NormalizeRequired(placementId, nameof(placementId))
            : null;
        WidthCells = NormalizeSpan(widthCells, nameof(widthCells));
        HeightCells = NormalizeSpan(heightCells, nameof(heightCells));
    }

    public ComponentPreviewKeyKind Kind { get; }

    public string ComponentTypeId { get; }

    public string? PlacementId { get; }

    public int WidthCells { get; }

    public int HeightCells { get; }

    public static ComponentPreviewKey ForComponentType(string componentTypeId, int widthCells, int heightCells)
    {
        return new ComponentPreviewKey(ComponentPreviewKeyKind.ComponentType, componentTypeId, null, widthCells, heightCells);
    }

    public static ComponentPreviewKey ForPlacementInstance(string componentTypeId, string placementId, int widthCells, int heightCells)
    {
        return new ComponentPreviewKey(
            ComponentPreviewKeyKind.PlacementInstance,
            componentTypeId,
            placementId,
            widthCells,
            heightCells);
    }

    public override string ToString()
    {
        return Kind == ComponentPreviewKeyKind.ComponentType
            ? $"Type:{ComponentTypeId}[{WidthCells}x{HeightCells}]"
            : $"Placement:{ComponentTypeId}@{PlacementId}[{WidthCells}x{HeightCells}]";
    }

    private static string NormalizeRequired(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        return value.Trim();
    }

    private static int NormalizeSpan(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Span must be greater than zero.");
        }

        return value;
    }
}

public enum ComponentPreviewImageState
{
    Pending = 0,
    Ready = 1,
    Failed = 2
}

public sealed class ComponentPreviewImageEntry : ObservableObject
{
    private IImage? _bitmap;
    private ComponentPreviewImageState _state = ComponentPreviewImageState.Pending;
    private string _visualSignature = string.Empty;
    private string? _errorMessage;
    private long _revision;
    private DateTimeOffset _lastUpdatedUtc = DateTimeOffset.UtcNow;

    public ComponentPreviewImageEntry(ComponentPreviewKey key, string? visualSignature = null)
    {
        Key = key;
        VisualSignature = NormalizeSignature(visualSignature);
    }

    public ComponentPreviewKey Key { get; }

    public IImage? Bitmap
    {
        get => _bitmap;
        private set => SetProperty(ref _bitmap, value);
    }

    public ComponentPreviewImageState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public string VisualSignature
    {
        get => _visualSignature;
        private set => SetProperty(ref _visualSignature, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public long Revision
    {
        get => _revision;
        private set => SetProperty(ref _revision, value);
    }

    public DateTimeOffset LastUpdatedUtc
    {
        get => _lastUpdatedUtc;
        private set => SetProperty(ref _lastUpdatedUtc, value);
    }

    internal long BeginGeneration(string visualSignature)
    {
        var normalizedVisualSignature = NormalizeSignature(visualSignature);
        var nextRevision = Revision + 1;
        Revision = nextRevision;
        VisualSignature = normalizedVisualSignature;
        State = ComponentPreviewImageState.Pending;
        ReplaceBitmap(null);
        ErrorMessage = null;
        LastUpdatedUtc = DateTimeOffset.UtcNow;
        return nextRevision;
    }

    internal bool TryApplyGeneratedBitmap(long expectedRevision, IImage bitmap, string visualSignature)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (Revision != expectedRevision)
        {
            DisposeIfNeeded(bitmap);
            return false;
        }

        VisualSignature = NormalizeSignature(visualSignature);
        State = ComponentPreviewImageState.Ready;
        ReplaceBitmap(bitmap);
        ErrorMessage = null;
        LastUpdatedUtc = DateTimeOffset.UtcNow;
        return true;
    }

    internal bool TryApplyFailure(long expectedRevision, string visualSignature, string? errorMessage)
    {
        if (Revision != expectedRevision)
        {
            return false;
        }

        VisualSignature = NormalizeSignature(visualSignature);
        State = ComponentPreviewImageState.Failed;
        ReplaceBitmap(null);
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Unknown preview generation failure." : errorMessage.Trim();
        LastUpdatedUtc = DateTimeOffset.UtcNow;
        return true;
    }

    internal void StoreBitmap(IImage bitmap, string visualSignature)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        Revision += 1;
        VisualSignature = NormalizeSignature(visualSignature);
        State = ComponentPreviewImageState.Ready;
        ReplaceBitmap(bitmap);
        ErrorMessage = null;
        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    internal void StoreFailure(string visualSignature, string? errorMessage)
    {
        Revision += 1;
        VisualSignature = NormalizeSignature(visualSignature);
        State = ComponentPreviewImageState.Failed;
        ReplaceBitmap(null);
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Unknown preview generation failure." : errorMessage.Trim();
        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    internal void Invalidate(string? visualSignature = null)
    {
        Revision += 1;
        if (visualSignature is not null)
        {
            VisualSignature = NormalizeSignature(visualSignature);
        }

        State = ComponentPreviewImageState.Pending;
        ReplaceBitmap(null);
        ErrorMessage = null;
        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    internal void DisposeBitmap()
    {
        ReplaceBitmap(null);
    }

    private void ReplaceBitmap(IImage? bitmap)
    {
        var previous = _bitmap;
        if (ReferenceEquals(previous, bitmap))
        {
            return;
        }

        Bitmap = bitmap;
        DisposeIfNeeded(previous);
    }

    private static void DisposeIfNeeded(IImage? bitmap)
    {
        if (bitmap is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string NormalizeSignature(string? visualSignature)
    {
        return visualSignature?.Trim() ?? string.Empty;
    }
}

internal sealed class ComponentPreviewKeyComparer : IEqualityComparer<ComponentPreviewKey>
{
    public static ComponentPreviewKeyComparer Instance { get; } = new();

    public bool Equals(ComponentPreviewKey x, ComponentPreviewKey y)
    {
        return x.Kind == y.Kind &&
               StringComparer.OrdinalIgnoreCase.Equals(x.ComponentTypeId, y.ComponentTypeId) &&
               StringComparer.OrdinalIgnoreCase.Equals(x.PlacementId, y.PlacementId) &&
               x.WidthCells == y.WidthCells &&
               x.HeightCells == y.HeightCells;
    }

    public int GetHashCode(ComponentPreviewKey obj)
    {
        var hash = new HashCode();
        hash.Add(obj.Kind);
        hash.Add(obj.ComponentTypeId, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.PlacementId, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.WidthCells);
        hash.Add(obj.HeightCells);
        return hash.ToHashCode();
    }
}
