using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace LanMountainDesktop.Launcher.ViewModels;

/// <summary>
/// 开发调试窗口 ViewModel
/// </summary>
public sealed class DevDebugWindowViewModel : INotifyPropertyChanged
{
    private bool _isSplashEnabled = true;
    private bool _isErrorEnabled = true;
    private bool _isUpdateEnabled = true;
    private bool _isOobeEnabled = true;
    private bool _isDataLocationEnabled = true;
    private string _statusMessage = "就绪";

    public event PropertyChangedEventHandler? PropertyChanged;

    #region 页面开关

    /// <summary>
    /// 启动画面是否启用实际功能
    /// </summary>
    public bool IsSplashEnabled
    {
        get => _isSplashEnabled;
        set
        {
            if (_isSplashEnabled != value)
            {
                _isSplashEnabled = value;
                OnPropertyChanged();
                UpdateStatus($"启动画面: {(value ? "功能模式" : "仅查看")}");
            }
        }
    }

    /// <summary>
    /// 错误页面是否启用实际功能
    /// </summary>
    public bool IsErrorEnabled
    {
        get => _isErrorEnabled;
        set
        {
            if (_isErrorEnabled != value)
            {
                _isErrorEnabled = value;
                OnPropertyChanged();
                UpdateStatus($"错误页面: {(value ? "功能模式" : "仅查看")}");
            }
        }
    }

    /// <summary>
    /// 更新页面是否启用实际功能
    /// </summary>
    public bool IsUpdateEnabled
    {
        get => _isUpdateEnabled;
        set
        {
            if (_isUpdateEnabled != value)
            {
                _isUpdateEnabled = value;
                OnPropertyChanged();
                UpdateStatus($"更新页面: {(value ? "功能模式" : "仅查看")}");
            }
        }
    }

    /// <summary>
    /// OOBE页面是否启用实际功能
    /// </summary>
    public bool IsOobeEnabled
    {
        get => _isOobeEnabled;
        set
        {
            if (_isOobeEnabled != value)
            {
                _isOobeEnabled = value;
                OnPropertyChanged();
                UpdateStatus($"OOBE页面: {(value ? "功能模式" : "仅查看")}");
            }
        }
    }

    /// <summary>
    /// 数据位置选择页面是否启用实际功能
    /// </summary>
    public bool IsDataLocationEnabled
    {
        get => _isDataLocationEnabled;
        set
        {
            if (_isDataLocationEnabled != value)
            {
                _isDataLocationEnabled = value;
                OnPropertyChanged();
                UpdateStatus($"数据位置选择: {(value ? "功能模式" : "仅查看")}");
            }
        }
    }

    #endregion

    #region 状态信息

    /// <summary>
    /// 状态消息
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region 命令

    /// <summary>
    /// 打开启动画面命令
    /// </summary>
    public ICommand OpenSplashCommand { get; }

    /// <summary>
    /// 打开错误页面命令
    /// </summary>
    public ICommand OpenErrorCommand { get; }

    /// <summary>
    /// 打开更新页面命令
    /// </summary>
    public ICommand OpenUpdateCommand { get; }

    /// <summary>
    /// 打开OOBE页面命令
    /// </summary>
    public ICommand OpenOobeCommand { get; }

    /// <summary>
    /// 打开数据位置选择页面命令
    /// </summary>
    public ICommand OpenDataLocationCommand { get; }

    /// <summary>
    /// 全部切换到查看模式命令
    /// </summary>
    public ICommand SetAllViewOnlyCommand { get; }

    /// <summary>
    /// 全部切换到功能模式命令
    /// </summary>
    public ICommand SetAllFunctionalCommand { get; }

    /// <summary>
    /// 关闭窗口命令
    /// </summary>
    public ICommand CloseCommand { get; }

    #endregion

    #region 事件

    /// <summary>
    /// 请求打开启动画面
    /// </summary>
    public event EventHandler<SplashOpenEventArgs>? OpenSplashRequested;

    /// <summary>
    /// 请求打开错误页面
    /// </summary>
    public event EventHandler<ErrorOpenEventArgs>? OpenErrorRequested;

    /// <summary>
    /// 请求打开更新页面
    /// </summary>
    public event EventHandler<UpdateOpenEventArgs>? OpenUpdateRequested;

    /// <summary>
    /// 请求打开OOBE页面
    /// </summary>
    public event EventHandler<OobeOpenEventArgs>? OpenOobeRequested;

    /// <summary>
    /// 请求打开数据位置选择页面
    /// </summary>
    public event EventHandler<DataLocationOpenEventArgs>? OpenDataLocationRequested;

    /// <summary>
    /// 请求关闭窗口
    /// </summary>
    public event EventHandler? CloseRequested;

    #endregion

    public DevDebugWindowViewModel()
    {
        OpenSplashCommand = new RelayCommand(() =>
        {
            OpenSplashRequested?.Invoke(this, new SplashOpenEventArgs(IsSplashEnabled));
        });

        OpenErrorCommand = new RelayCommand(() =>
        {
            OpenErrorRequested?.Invoke(this, new ErrorOpenEventArgs(IsErrorEnabled));
        });

        OpenUpdateCommand = new RelayCommand(() =>
        {
            OpenUpdateRequested?.Invoke(this, new UpdateOpenEventArgs(IsUpdateEnabled));
        });

        OpenOobeCommand = new RelayCommand(() =>
        {
            OpenOobeRequested?.Invoke(this, new OobeOpenEventArgs(IsOobeEnabled));
        });

        OpenDataLocationCommand = new RelayCommand(() =>
        {
            OpenDataLocationRequested?.Invoke(this, new DataLocationOpenEventArgs(IsDataLocationEnabled));
        });

        SetAllViewOnlyCommand = new RelayCommand(() =>
        {
            IsSplashEnabled = false;
            IsErrorEnabled = false;
            IsUpdateEnabled = false;
            IsOobeEnabled = false;
            IsDataLocationEnabled = false;
            UpdateStatus("全部页面已切换到查看模式");
        });

        SetAllFunctionalCommand = new RelayCommand(() =>
        {
            IsSplashEnabled = true;
            IsErrorEnabled = true;
            IsUpdateEnabled = true;
            IsOobeEnabled = true;
            IsDataLocationEnabled = true;
            UpdateStatus("全部页面已切换到功能模式");
        });

        CloseCommand = new RelayCommand(() =>
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private void UpdateStatus(string message)
    {
        StatusMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

#region 事件参数

public class SplashOpenEventArgs : EventArgs
{
    public bool IsFunctional { get; }
    public SplashOpenEventArgs(bool isFunctional) => IsFunctional = isFunctional;
}

public class ErrorOpenEventArgs : EventArgs
{
    public bool IsFunctional { get; }
    public ErrorOpenEventArgs(bool isFunctional) => IsFunctional = isFunctional;
}

public class UpdateOpenEventArgs : EventArgs
{
    public bool IsFunctional { get; }
    public UpdateOpenEventArgs(bool isFunctional) => IsFunctional = isFunctional;
}

public class OobeOpenEventArgs : EventArgs
{
    public bool IsFunctional { get; }
    public OobeOpenEventArgs(bool isFunctional) => IsFunctional = isFunctional;
}

public class DataLocationOpenEventArgs : EventArgs
{
    public bool IsFunctional { get; }
    public DataLocationOpenEventArgs(bool isFunctional) => IsFunctional = isFunctional;
}

#endregion
