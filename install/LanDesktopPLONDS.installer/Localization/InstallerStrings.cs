namespace LanDesktopPLONDS.Installer.Localization;

/// <summary>
/// 安装器本地化字符串映射。
/// 将服务层报告的英文阶段键映射为中文显示文本。
/// 未匹配的英文键原样透传，避免丢失信息。
/// </summary>
internal static class InstallerStrings
{
    /// <summary>
    /// 已知英文阶段键 → 中文显示文本。
    /// 键比较使用 OrdinalIgnoreCase，容错服务端大小写差异。
    /// </summary>
    private static readonly Dictionary<string, string> StageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Downloading Files.zip"] = "正在下载 Files.zip",
        ["Files package prepared"] = "文件包已准备就绪",
        ["Creating deployment"] = "正在创建部署目录",
        ["Activating deployment"] = "正在激活部署",
        ["Copying files"] = "正在复制文件",
        ["Copying launcher files"] = "正在复制启动器文件",
        ["Completed"] = "安装完成",
    };

    /// <summary>
    /// 将英文阶段键翻译为中文。未匹配的键原样返回。
    /// </summary>
    public static string TranslateStage(string stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            return stage;
        }

        return StageMap.TryGetValue(stage, out var translated) ? translated : stage;
    }
}
