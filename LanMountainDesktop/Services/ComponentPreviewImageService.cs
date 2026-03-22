using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;

namespace LanMountainDesktop.Services;

public sealed class ComponentPreviewImageService : IComponentPreviewImageService
{
    private readonly object _gate = new();
    private readonly Dictionary<ComponentPreviewKey, ComponentPreviewImageEntry> _entries = new(ComponentPreviewKeyComparer.Instance);
    private readonly Dictionary<ComponentPreviewKey, Task<ComponentPreviewImageEntry>> _inFlightRequests = new(ComponentPreviewKeyComparer.Instance);
    private Task _queueTail = Task.CompletedTask;

    public ComponentPreviewImageEntry GetOrCreateEntry(ComponentPreviewKey key, string? visualSignature = null)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var created = new ComponentPreviewImageEntry(key, visualSignature);
            _entries[key] = created;
            return created;
        }
    }

    public bool TryGetEntry(ComponentPreviewKey key, out ComponentPreviewImageEntry? entry)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                entry = existing;
                return true;
            }

            entry = null;
            return false;
        }
    }

    public IReadOnlyCollection<ComponentPreviewImageEntry> GetEntriesSnapshot()
    {
        lock (_gate)
        {
            return _entries.Values.ToArray();
        }
    }

    public Task<ComponentPreviewImageEntry> QueueGenerationAsync(
        ComponentPreviewKey key,
        string visualSignature,
        Func<CancellationToken, Task<IImage?>> generationWork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generationWork);

        var normalizedSignature = NormalizeRequired(visualSignature, nameof(visualSignature));
        lock (_gate)
        {
            var entry = GetOrCreateEntryCore(key);

            if (entry.State == ComponentPreviewImageState.Ready &&
                entry.Bitmap is not null &&
                StringComparer.Ordinal.Equals(entry.VisualSignature, normalizedSignature))
            {
                return Task.FromResult(entry);
            }

            if (_inFlightRequests.TryGetValue(key, out var inFlight))
            {
                return inFlight;
            }

            var expectedRevision = entry.BeginGeneration(normalizedSignature);
            var previousTask = _queueTail;
            var queuedTask = RunGenerationAsync(
                previousTask,
                key,
                entry,
                expectedRevision,
                normalizedSignature,
                generationWork,
                cancellationToken);

            _inFlightRequests[key] = queuedTask;
            _queueTail = queuedTask.ContinueWith(
                static _ => { },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return queuedTask;
        }
    }

    public ComponentPreviewImageEntry Store(ComponentPreviewKey key, IImage bitmap, string visualSignature)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var normalizedSignature = NormalizeRequired(visualSignature, nameof(visualSignature));
        lock (_gate)
        {
            var entry = GetOrCreateEntryCore(key);
            entry.StoreBitmap(bitmap, normalizedSignature);
            _inFlightRequests.Remove(key);
            return entry;
        }
    }

    public ComponentPreviewImageEntry StoreFailure(ComponentPreviewKey key, string visualSignature, string? errorMessage = null)
    {
        var normalizedSignature = NormalizeRequired(visualSignature, nameof(visualSignature));
        lock (_gate)
        {
            var entry = GetOrCreateEntryCore(key);
            entry.StoreFailure(normalizedSignature, errorMessage);
            _inFlightRequests.Remove(key);
            return entry;
        }
    }

    public bool Invalidate(ComponentPreviewKey key, string? visualSignature = null)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                return false;
            }

            entry.Invalidate(visualSignature);
            _inFlightRequests.Remove(key);
            return true;
        }
    }

    public int RemovePlacementPreviews(string placementId)
    {
        var normalizedPlacementId = NormalizeRequired(placementId, nameof(placementId));
        lock (_gate)
        {
            var entriesToRemove = _entries
                .Where(static pair => pair.Key.Kind == ComponentPreviewKeyKind.PlacementInstance)
                .Where(pair => StringComparer.OrdinalIgnoreCase.Equals(pair.Key.PlacementId, normalizedPlacementId))
                .ToArray();

            foreach (var pair in entriesToRemove)
            {
                pair.Value.DisposeBitmap();
                _entries.Remove(pair.Key);
                _inFlightRequests.Remove(pair.Key);
            }

            return entriesToRemove.Length;
        }
    }

    public int InvalidateVisualSignature(string visualSignature)
    {
        var normalizedSignature = NormalizeRequired(visualSignature, nameof(visualSignature));
        lock (_gate)
        {
            var entriesToInvalidate = _entries.Values
                .Where(entry => StringComparer.Ordinal.Equals(entry.VisualSignature, normalizedSignature))
                .ToArray();

            foreach (var entry in entriesToInvalidate)
            {
                entry.Invalidate(normalizedSignature);
                _inFlightRequests.Remove(entry.Key);
            }

            return entriesToInvalidate.Length;
        }
    }

    private async Task<ComponentPreviewImageEntry> RunGenerationAsync(
        Task previousTask,
        ComponentPreviewKey key,
        ComponentPreviewImageEntry entry,
        long expectedRevision,
        string visualSignature,
        Func<CancellationToken, Task<IImage?>> generationWork,
        CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await previousTask.ConfigureAwait(false);
            }
            catch
            {
                // Keep serial queue processing even if previous work faulted.
            }

            IImage? bitmap;
            try
            {
                bitmap = await generationWork(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    entry.TryApplyFailure(expectedRevision, visualSignature, ex.Message);
                }

                return entry;
            }

            lock (_gate)
            {
                if (bitmap is null)
                {
                    entry.TryApplyFailure(expectedRevision, visualSignature, "Preview generation returned no bitmap.");
                }
                else
                {
                    entry.TryApplyGeneratedBitmap(expectedRevision, bitmap, visualSignature);
                }
            }

            return entry;
        }
        finally
        {
            lock (_gate)
            {
                _inFlightRequests.Remove(key);
            }
        }
    }

    private ComponentPreviewImageEntry GetOrCreateEntryCore(ComponentPreviewKey key)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = new ComponentPreviewImageEntry(key);
        _entries[key] = created;
        return created;
    }

    private static string NormalizeRequired(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        return value.Trim();
    }
}
