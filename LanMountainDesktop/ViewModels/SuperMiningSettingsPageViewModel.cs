using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FluentIcons.Common;

namespace LanMountainDesktop.ViewModels;

public sealed class SuperMiningSettingsPageViewModel : INotifyPropertyChanged
{
    private double _hashRate = 125.6;
    private string _coinsMined = "0.08923";
    private int _poolConnections = 98;
    private double _miningProgress;
    private string _miningStatus = "正在挖矿中...";
    private bool _showAprilFoolsHint;
    private Bitmap? _qrCodeImage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Symbol ActionSymbol => Symbol.ArrowDownload;

    public double HashRate
    {
        get => _hashRate;
        set
        {
            if (Math.Abs(_hashRate - value) > 0.01)
            {
                _hashRate = value;
                OnPropertyChanged();
            }
        }
    }

    public string CoinsMined
    {
        get => _coinsMined;
        set
        {
            if (_coinsMined != value)
            {
                _coinsMined = value;
                OnPropertyChanged();
            }
        }
    }

    public int PoolConnections
    {
        get => _poolConnections;
        set
        {
            if (_poolConnections != value)
            {
                _poolConnections = value;
                OnPropertyChanged();
            }
        }
    }

    public double MiningProgress
    {
        get => _miningProgress;
        set
        {
            if (Math.Abs(_miningProgress - value) > 0.1)
            {
                _miningProgress = value;
                OnPropertyChanged();
            }
        }
    }

    public string MiningStatus
    {
        get => _miningStatus;
        set
        {
            if (_miningStatus != value)
            {
                _miningStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowAprilFoolsHint
    {
        get => _showAprilFoolsHint;
        set
        {
            if (_showAprilFoolsHint != value)
            {
                _showAprilFoolsHint = value;
                OnPropertyChanged();
            }
        }
    }

    public Bitmap? QrCodeImage
    {
        get => _qrCodeImage;
        set
        {
            if (_qrCodeImage != value)
            {
                _qrCodeImage = value;
                OnPropertyChanged();
            }
        }
    }

    public void LoadQrCodeImage()
    {
        try
        {
            var assets = AssetLoader.Open(new System.Uri("avares://LanMountainDesktop/Assets/mining_qrcode.png"));
            QrCodeImage = new Bitmap(assets);
        }
        catch
        {
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
