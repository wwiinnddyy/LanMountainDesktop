using System;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "super-mining",
    "超级挖矿",
    SettingsPageCategory.About,
    IconKey = "Savings",
    SortOrder = 35,
    TitleLocalizationKey = "settings.supermining.title",
    DescriptionLocalizationKey = "settings.supermining.description",
    HidePageTitle = true)]
public partial class SuperMiningSettingsPage : SettingsPageBase
{
    private readonly DispatcherTimer _updateTimer;
    private readonly Random _random = new();
    private int _tickCount;

    public SuperMiningSettingsPage()
        : this(new SuperMiningSettingsPageViewModel())
    {
    }

    public SuperMiningSettingsPage(SuperMiningSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();

        ViewModel.LoadQrCodeImage();

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _updateTimer.Tick += OnUpdateTimerTick;

        Unloaded += OnUnloaded;
    }

    public SuperMiningSettingsPageViewModel ViewModel { get; }

    public override void OnNavigatedTo(object? parameter)
    {
        base.OnNavigatedTo(parameter);
        _updateTimer.Start();
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _updateTimer.Stop();
    }

    private void OnUpdateTimerTick(object? sender, EventArgs e)
    {
        _tickCount++;

        ViewModel.HashRate = 125.6 + _random.NextDouble() * 10 - 5;
        ViewModel.MiningProgress = (ViewModel.MiningProgress + 1) % 100;

        if (_tickCount % 5 == 0)
        {
            var baseCoins = 0.08923;
            var increment = _random.NextDouble() * 0.00001;
            ViewModel.CoinsMined = (baseCoins + increment).ToString("F5");
        }

        ViewModel.PoolConnections = _random.Next(95, 100);

        var statuses = new[]
        {
            "正在挖矿中...",
            "矿池连接稳定",
            "正在提交份额...",
            "算力优化中...",
            "收益计算中..."
        };
        ViewModel.MiningStatus = statuses[_tickCount % statuses.Length];

        if (DateTime.Now.Month == 4 && DateTime.Now.Day == 1)
        {
            ViewModel.ShowAprilFoolsHint = true;
        }
    }
}
