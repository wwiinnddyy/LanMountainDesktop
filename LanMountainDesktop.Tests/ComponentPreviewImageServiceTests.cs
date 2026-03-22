using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ComponentPreviewImageServiceTests
{
    [Fact]
    public async Task QueueGenerationAsync_ExecutesWorkSeriallyAcrossKeys()
    {
        var service = new ComponentPreviewImageService();
        var executionOrder = new List<string>();
        var activeCount = 0;
        var maxActiveCount = 0;

        Task<ComponentPreviewImageEntry> Queue(string componentTypeId)
        {
            var key = ComponentPreviewKey.ForComponentType(componentTypeId, widthCells: 2, heightCells: 2);
            return service.QueueGenerationAsync(
                key,
                visualSignature: $"sig:{componentTypeId}",
                async _ =>
                {
                    var activeNow = Interlocked.Increment(ref activeCount);
                    maxActiveCount = Math.Max(maxActiveCount, activeNow);
                    lock (executionOrder)
                    {
                        executionOrder.Add(componentTypeId);
                    }

                    await Task.Delay(40);
                    Interlocked.Decrement(ref activeCount);
                    return CreateImage();
                });
        }

        var first = Queue("Clock");
        var second = Queue("Weather");
        var third = Queue("Calendar");

        await Task.WhenAll(first, second, third);

        Assert.Equal(1, maxActiveCount);
        Assert.Equal(["Clock", "Weather", "Calendar"], executionOrder);
    }

    [Fact]
    public async Task QueueGenerationAsync_DeduplicatesConcurrentRequestsForSameKey()
    {
        var service = new ComponentPreviewImageService();
        var key = ComponentPreviewKey.ForComponentType("Clock", widthCells: 2, heightCells: 2);
        var generationCount = 0;
        var bitmap = CreateImage();
        var completion = new TaskCompletionSource<IImage?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<IImage?> Generation(CancellationToken _)
        {
            Interlocked.Increment(ref generationCount);
            return completion.Task;
        }

        var first = service.QueueGenerationAsync(key, "clock-sig", Generation);
        var second = service.QueueGenerationAsync(key, "clock-sig", Generation);

        Assert.Same(first, second);

        completion.SetResult(bitmap);
        var entry = await first;

        Assert.Equal(1, generationCount);
        Assert.Equal(ComponentPreviewImageState.Ready, entry.State);
        Assert.Same(bitmap, entry.Bitmap);
    }

    [Fact]
    public void Invalidate_ResetsSingleKeyToPending()
    {
        var service = new ComponentPreviewImageService();
        var key = ComponentPreviewKey.ForComponentType("Clock", widthCells: 2, heightCells: 2);
        var image = CreateDisposableImage();
        var stored = service.Store(key, image, "clock-sig");
        var previousRevision = stored.Revision;

        var result = service.Invalidate(key);

        Assert.True(result);
        Assert.Equal(ComponentPreviewImageState.Pending, stored.State);
        Assert.Null(stored.Bitmap);
        Assert.True(image.IsDisposed);
        Assert.True(stored.Revision > previousRevision);
        Assert.Equal("clock-sig", stored.VisualSignature);
    }

    [Fact]
    public void RemovePlacementPreviews_RemovesOnlyMatchingPlacementEntries()
    {
        var service = new ComponentPreviewImageService();

        var removedClock = ComponentPreviewKey.ForPlacementInstance("Clock", "desk-1", widthCells: 2, heightCells: 2);
        var removedWeather = ComponentPreviewKey.ForPlacementInstance("Weather", "desk-1", widthCells: 4, heightCells: 2);
        var keptPlacement = ComponentPreviewKey.ForPlacementInstance("Clock", "desk-2", widthCells: 2, heightCells: 2);
        var keptType = ComponentPreviewKey.ForComponentType("Clock", widthCells: 2, heightCells: 2);
        var removedClockImage = CreateDisposableImage();
        var removedWeatherImage = CreateDisposableImage();
        var keptPlacementImage = CreateDisposableImage();
        var keptTypeImage = CreateDisposableImage();

        service.Store(removedClock, removedClockImage, "sig-a");
        service.Store(removedWeather, removedWeatherImage, "sig-b");
        service.Store(keptPlacement, keptPlacementImage, "sig-c");
        service.Store(keptType, keptTypeImage, "sig-d");

        var removedCount = service.RemovePlacementPreviews("desk-1");

        Assert.Equal(2, removedCount);
        Assert.False(service.TryGetEntry(removedClock, out _));
        Assert.False(service.TryGetEntry(removedWeather, out _));
        Assert.True(service.TryGetEntry(keptPlacement, out _));
        Assert.True(service.TryGetEntry(keptType, out _));
        Assert.True(removedClockImage.IsDisposed);
        Assert.True(removedWeatherImage.IsDisposed);
        Assert.False(keptPlacementImage.IsDisposed);
        Assert.False(keptTypeImage.IsDisposed);
    }

    [Fact]
    public void InvalidateVisualSignature_InvalidatesEveryMatchingEntry()
    {
        var service = new ComponentPreviewImageService();
        const string matchingSignature = "shared-sig";
        const string otherSignature = "other-sig";

        var first = service.Store(
            ComponentPreviewKey.ForComponentType("Clock", widthCells: 2, heightCells: 2),
            CreateImage(),
            matchingSignature);
        var second = service.Store(
            ComponentPreviewKey.ForPlacementInstance("Clock", "desk-1", widthCells: 2, heightCells: 2),
            CreateImage(),
            matchingSignature);
        var third = service.Store(
            ComponentPreviewKey.ForComponentType("Weather", widthCells: 2, heightCells: 1),
            CreateImage(),
            otherSignature);

        var invalidatedCount = service.InvalidateVisualSignature(matchingSignature);

        Assert.Equal(2, invalidatedCount);
        Assert.Equal(ComponentPreviewImageState.Pending, first.State);
        Assert.Equal(ComponentPreviewImageState.Pending, second.State);
        Assert.Null(first.Bitmap);
        Assert.Null(second.Bitmap);
        Assert.Equal(ComponentPreviewImageState.Ready, third.State);
        Assert.NotNull(third.Bitmap);
    }

    [Fact]
    public void Store_ReplacingBitmap_DisposesPreviousBitmap_WhenInstanceChanges()
    {
        var service = new ComponentPreviewImageService();
        var key = ComponentPreviewKey.ForComponentType("Clock", widthCells: 2, heightCells: 2);
        var first = CreateDisposableImage();
        var second = CreateDisposableImage();

        service.Store(key, first, "sig-a");
        service.Store(key, second, "sig-b");

        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void Store_ReplacingBitmap_DoesNotDispose_WhenSameInstanceReused()
    {
        var service = new ComponentPreviewImageService();
        var key = ComponentPreviewKey.ForComponentType("Clock", widthCells: 2, heightCells: 2);
        var image = CreateDisposableImage();

        service.Store(key, image, "sig-a");
        service.Store(key, image, "sig-b");

        Assert.False(image.IsDisposed);
    }

    [Fact]
    public void StoreFailure_DisposesExistingBitmap()
    {
        var service = new ComponentPreviewImageService();
        var key = ComponentPreviewKey.ForComponentType("Clock", widthCells: 2, heightCells: 2);
        var image = CreateDisposableImage();

        service.Store(key, image, "sig-a");
        var entry = service.StoreFailure(key, "sig-a", "failed");

        Assert.True(image.IsDisposed);
        Assert.Equal(ComponentPreviewImageState.Failed, entry.State);
        Assert.Null(entry.Bitmap);
    }

    [Fact]
    public async Task QueueGenerationAsync_DisposesStaleGeneratedBitmap_WhenEntryWasInvalidated()
    {
        var service = new ComponentPreviewImageService();
        var key = ComponentPreviewKey.ForComponentType("Clock", widthCells: 2, heightCells: 2);
        var completion = new TaskCompletionSource<IImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = CreateDisposableImage();

        var generationTask = service.QueueGenerationAsync(key, "sig-a", _ => completion.Task);
        _ = service.Invalidate(key);
        completion.SetResult(stale);
        var entry = await generationTask;

        Assert.True(stale.IsDisposed);
        Assert.Equal(ComponentPreviewImageState.Pending, entry.State);
        Assert.Null(entry.Bitmap);
    }

    private static IImage CreateImage() => new TestImage();
    private static DisposableTestImage CreateDisposableImage() => new();

    private sealed class TestImage : IImage
    {
        public Size Size => new(1, 1);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            _ = context;
            _ = sourceRect;
            _ = destRect;
        }
    }

    private sealed class DisposableTestImage : IImage, IDisposable
    {
        public Size Size => new(1, 1);

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            _ = context;
            _ = sourceRect;
            _ = destRect;
        }
    }
}
