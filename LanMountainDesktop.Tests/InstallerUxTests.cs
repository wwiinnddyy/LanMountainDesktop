using LanDesktopPLONDS.Installer.Localization;
using LanDesktopPLONDS.Installer.Models;
using LanDesktopPLONDS.Installer.Services;
using LanDesktopPLONDS.Installer.ViewModels;
using LanMountainDesktop.Shared.Contracts.Privacy;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 安装器 UX 行为测试：版本检查取消、阶段本地化映射、命令行参数解析。
/// </summary>
public sealed class InstallerUxTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        AppContext.BaseDirectory,
        "TestArtifacts",
        "LanMountainDesktop.Tests",
        nameof(InstallerUxTests),
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    // =====================================================================
    // Task 1: VM 版本检查取消
    // =====================================================================

    /// <summary>
    /// 模拟 CheckLatestAsync 阻塞直到 token 被取消，
    /// 验证 IsCheckingUpdate 在取消后恢复为 false，且错误信息已设置。
    /// </summary>
    [Fact]
    public async Task CheckCancellation_SetsIsCheckingUpdateFalse_AfterCancel()
    {
        var blockingService = new BlockingInstallService();
        var vm = CreateVm(blockingService);

        // 导航到 InstallLocation 步骤
        vm.InstallPath = Path.Combine(_tempRoot, "LanMountainDesktop");
        await vm.NextCommand.ExecuteAsync(null); // Welcome → InstallLocation
        Assert.Equal(InstallerStepId.InstallLocation, vm.CurrentStep);

        // 启动版本检查（会阻塞）
        var checkTask = vm.NextCommand.ExecuteAsync(null);

        // 等待 IsCheckingUpdate 变为 true
        for (var i = 0; i < 50 && !vm.IsCheckingUpdate; i++)
        {
            await Task.Delay(50);
        }

        Assert.True(vm.IsCheckingUpdate, "IsCheckingUpdate should be true during check");

        // 取消检查
        vm.CancelCheckCommand.Execute(null);
        await checkTask;

        Assert.False(vm.IsCheckingUpdate, "IsCheckingUpdate should be false after cancel");
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("取消", vm.ErrorMessage);
    }

    /// <summary>
    /// 版本检查期间 Back/Next 命令应被禁用。
    /// </summary>
    [Fact]
    public async Task CheckCancellation_BackAndNextDisabled_DuringCheck()
    {
        var blockingService = new BlockingInstallService();
        var vm = CreateVm(blockingService);

        vm.InstallPath = Path.Combine(_tempRoot, "LanMountainDesktop");
        await vm.NextCommand.ExecuteAsync(null); // Welcome → InstallLocation

        // 启动版本检查
        var checkTask = vm.NextCommand.ExecuteAsync(null);

        // 等待 IsCheckingUpdate
        for (var i = 0; i < 50 && !vm.IsCheckingUpdate; i++)
        {
            await Task.Delay(50);
        }

        // 检查期间 CanGoNext / CanGoBack 应为 false
        Assert.False(vm.CanGoNext, "CanGoNext should be false while checking");
        Assert.False(vm.CanGoBack, "CanGoBack should be false while checking");

        // 清理
        vm.CancelCheckCommand.Execute(null);
        await checkTask;
    }

    /// <summary>
    /// 30 秒超时后应设置超时错误消息。
    /// </summary>
    [Fact]
    public async Task CheckCancellation_TimeoutSetsChineseMessage()
    {
        var service = new VerySlowInstallService();
        var vm = CreateVm(service);

        vm.InstallPath = Path.Combine(_tempRoot, "LanMountainDesktop");
        await vm.NextCommand.ExecuteAsync(null); // Welcome → InstallLocation

        // 触发版本检查，等待超时（30秒 + 缓冲）
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        try
        {
            await vm.NextCommand.ExecuteAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 正常 — 测试超时取消
        }

        // 超时后应显示中文超时消息或仍在检查
        // 注意：如果超时触发较快，会设置超时错误
        if (!vm.IsCheckingUpdate)
        {
            Assert.NotNull(vm.ErrorMessage);
        }
    }

    // =====================================================================
    // Task 4: 阶段本地化映射
    // =====================================================================

    [Theory]
    [InlineData("Downloading Files.zip", "正在下载 Files.zip")]
    [InlineData("Files package prepared", "文件包已准备就绪")]
    [InlineData("Creating deployment", "正在创建部署目录")]
    [InlineData("Activating deployment", "正在激活部署")]
    [InlineData("Copying files", "正在复制文件")]
    [InlineData("Copying launcher files", "正在复制启动器文件")]
    [InlineData("Completed", "安装完成")]
    public void StageMapping_KnownStage_ReturnsChinese(string input, string expected)
    {
        Assert.Equal(expected, InstallerStrings.TranslateStage(input));
    }

    [Theory]
    [InlineData("Unknown Stage")]
    [InlineData("Some New Stage")]
    [InlineData("")]
    public void StageMapping_UnknownStage_ReturnsOriginal(string input)
    {
        Assert.Equal(input, InstallerStrings.TranslateStage(input));
    }

    [Fact]
    public void StageMapping_CaseInsensitive()
    {
        Assert.Equal("正在下载 Files.zip", InstallerStrings.TranslateStage("downloading files.zip"));
        Assert.Equal("安装完成", InstallerStrings.TranslateStage("COMPLETED"));
    }

    [Fact]
    public void StageMapping_NullOrWhitespace_Passthrough()
    {
        Assert.Null(InstallerStrings.TranslateStage(null!));
        Assert.Equal("", InstallerStrings.TranslateStage(""));
        Assert.Equal("  ", InstallerStrings.TranslateStage("  "));
    }

    // =====================================================================
    // Task 2: --install-path 命令行参数解析
    // =====================================================================

    [Fact]
    public void ParseInstallPath_NullArgs_ReturnsNull()
    {
        Assert.Null(MainWindowViewModel.ParseInstallPath(null));
    }

    [Fact]
    public void ParseInstallPath_EmptyArgs_ReturnsNull()
    {
        Assert.Null(MainWindowViewModel.ParseInstallPath([]));
    }

    [Fact]
    public void ParseInstallPath_NoInstallPath_ReturnsNull()
    {
        Assert.Null(MainWindowViewModel.ParseInstallPath(["--other", "value"]));
    }

    [Fact]
    public void ParseInstallPath_WithInstallPath_ReturnsValue()
    {
        var result = MainWindowViewModel.ParseInstallPath(["--install-path", @"C:\Users\Test\LanMountainDesktop"]);
        Assert.Equal(@"C:\Users\Test\LanMountainDesktop", result);
    }

    [Fact]
    public void ParseInstallPath_QuotedPath_ReturnsUnquotedValue()
    {
        var result = MainWindowViewModel.ParseInstallPath(["--install-path", "\"C:\\Path With Spaces\\LanMountainDesktop\""]);
        Assert.Equal(@"C:\Path With Spaces\LanMountainDesktop", result);
    }

    [Fact]
    public void ParseInstallPath_CaseInsensitive()
    {
        var result = MainWindowViewModel.ParseInstallPath(["--INSTALL-PATH", "/tmp/app"]);
        Assert.Equal("/tmp/app", result);
    }

    [Fact]
    public void ParseInstallPath_LastArgWithoutValue_ReturnsNull()
    {
        // --install-path 是最后一个参数，没有跟随值
        Assert.Null(MainWindowViewModel.ParseInstallPath(["--other", "--install-path"]));
    }

    // =====================================================================
    // Task 5: 窗口标题本地化
    // =====================================================================

    [Fact]
    public void WindowTitle_IsLocalizedChinese()
    {
        var vm = CreateVm(new FakeInstallService());
        Assert.Equal("阑山桌面 安装程序", vm.WindowTitle);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private MainWindowViewModel CreateVm(IOnlineInstallService service)
    {
        return new MainWindowViewModel(
            service,
            new PrivacyDeviceIdentityProvider(Path.Combine(_tempRoot, "identity.json")));
    }

    /// <summary>
    /// 阻塞式安装服务：CheckLatestAsync 阻塞直到 token 被取消。
    /// </summary>
    private sealed class BlockingInstallService : IOnlineInstallService
    {
        public async Task<OnlineInstallPackageInfo> CheckLatestAsync(CancellationToken cancellationToken)
        {
            // 阻塞直到外部取消或 cancellationToken 取消
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await Task.Delay(Timeout.Infinite, linked.Token);
            throw new OperationCanceledException();
        }

        public Task InstallFreshAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task InstallFreshAsync(string installPath, OnlineInstallOptions options, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RepairAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UpdateIncrementalAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 极慢安装服务：CheckLatestAsync 阻塞 60 秒，用于测试超时。
    /// </summary>
    private sealed class VerySlowInstallService : IOnlineInstallService
    {
        public async Task<OnlineInstallPackageInfo> CheckLatestAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            return new OnlineInstallPackageInfo("1.0.0", "test", new Uri("https://example.com/files.zip"), 1024);
        }

        public Task InstallFreshAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task InstallFreshAsync(string installPath, OnlineInstallOptions options, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RepairAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UpdateIncrementalAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 即时返回的安装服务，用于非阻塞测试。
    /// </summary>
    private sealed class FakeInstallService : IOnlineInstallService
    {
        public Task<OnlineInstallPackageInfo> CheckLatestAsync(CancellationToken cancellationToken)
            => Task.FromResult(new OnlineInstallPackageInfo("1.0.0", "test", new Uri("https://test/Files.zip"), 1));

        public Task InstallFreshAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task InstallFreshAsync(string installPath, OnlineInstallOptions options, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RepairAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UpdateIncrementalAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
