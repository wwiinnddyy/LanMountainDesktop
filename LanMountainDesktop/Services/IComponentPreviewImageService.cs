using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;

namespace LanMountainDesktop.Services;

public interface IComponentPreviewImageService
{
    ComponentPreviewImageEntry GetOrCreateEntry(ComponentPreviewKey key, string? visualSignature = null);

    bool TryGetEntry(ComponentPreviewKey key, out ComponentPreviewImageEntry? entry);

    IReadOnlyCollection<ComponentPreviewImageEntry> GetEntriesSnapshot();

    Task<ComponentPreviewImageEntry> QueueGenerationAsync(
        ComponentPreviewKey key,
        string visualSignature,
        Func<CancellationToken, Task<IImage?>> generationWork,
        CancellationToken cancellationToken = default);

    ComponentPreviewImageEntry Store(ComponentPreviewKey key, IImage bitmap, string visualSignature);

    ComponentPreviewImageEntry StoreFailure(ComponentPreviewKey key, string visualSignature, string? errorMessage = null);

    bool Invalidate(ComponentPreviewKey key, string? visualSignature = null);

    int RemovePlacementPreviews(string placementId);

    int InvalidateVisualSignature(string visualSignature);
}
