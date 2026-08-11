namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// 安装程序单实例互斥锁。
/// 使用命名互斥锁确保只有一个安装程序实例在运行。
/// </summary>
public sealed class InstallerSingleInstance : IDisposable
{
    /// <summary>
    /// 全局互斥锁名称。
    /// </summary>
    public const string MutexName = @"Global\LanDesktopPLONDS.Installer";

    private Mutex? _mutex;
    private bool _disposed;

    /// <summary>
    /// 尝试获取单实例互斥锁。
    /// </summary>
    /// <returns>如果成功获取锁返回 true；如果已有实例运行返回 false。</returns>
    public bool TryAcquire()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InstallerSingleInstance));
        }

        _mutex = new Mutex(false, MutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 释放互斥锁。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // 释放已释放的互斥锁时忽略
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
