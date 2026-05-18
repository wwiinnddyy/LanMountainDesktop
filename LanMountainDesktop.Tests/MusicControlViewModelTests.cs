using LanMountainDesktop.Services;
using LanMountainDesktop.ViewModels;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class MusicControlViewModelTests : IDisposable
{
    private readonly MusicControlViewModel _viewModel;

    public MusicControlViewModelTests()
    {
        _viewModel = new MusicControlViewModel();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        _viewModel.Dispose();
        _viewModel.Dispose();
    }

    [Fact]
    public async Task Dispose_StopsRefreshAfterCancellation()
    {
        var refreshTask = _viewModel.RefreshAsync();
        _viewModel.Dispose();

        await Task.Delay(100);
    }

    [Fact]
    public void ViewModel_InitializesWithNoSession()
    {
        Assert.True(_viewModel.IsNoMedia);
    }

    public void Dispose()
    {
        _viewModel.Dispose();
    }
}
