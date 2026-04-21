namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 主程序发现选项
/// </summary>
public sealed class HostDiscoveryOptions
{
    /// <summary>
    /// 可执行文件名（Windows 下自动添加 .exe）
    /// </summary>
    public string ExecutableName { get; set; } = "LanMountainDesktop";

    /// <summary>
    /// 额外的搜索路径（支持通配符）
    /// </summary>
    public List<string> AdditionalSearchPaths { get; set; } = new();

    /// <summary>
    /// 是否递归搜索子目录
    /// </summary>
    public bool RecursiveSearch { get; set; } = false;

    /// <summary>
    /// 递归搜索的最大深度
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 3;

    /// <summary>
    /// 环境变量名称，用于指定自定义路径
    /// </summary>
    public string? CustomPathEnvVar { get; set; } = "LMD_HOST_PATH";

    /// <summary>
    /// 配置文件路径（相对于 app root）
    /// </summary>
    public string? ConfigFileName { get; set; } = "host-discovery.json";

    /// <summary>
    /// 是否优先使用开发模式配置
    /// </summary>
    public bool PreferDevModeConfig { get; set; } = true;

    /// <summary>
    /// 搜索超时（毫秒）
    /// </summary>
    public int SearchTimeoutMs { get; set; } = 5000;
}
