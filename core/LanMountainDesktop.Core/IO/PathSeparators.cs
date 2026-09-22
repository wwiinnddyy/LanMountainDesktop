namespace LanMountainDesktop.Shared.IO;

/// <summary>
/// 目录路径"末尾要不要带分隔符"这件事只认这一处。
/// 收口前 <c>EnsureTrailingSeparator</c> 有 4 份逐字相同的复制，散在 Core、宿主、启动器三个二进制里
/// （调用点 7 处），而 <c>AirAppLoader</c> 那份还偷偷多做了一步 <c>Path.GetFullPath</c>——
/// 绝对化是调用点的语义，混进这个 helper 会让"补个斜杠"变成"可能抛参数异常"。
/// 分隔符用 <see cref="Path.DirectorySeparatorChar"/>，所以 Linux 上是 <c>/</c>、Windows 上是 <c>\</c>。
/// </summary>
public static class PathSeparators
{
    /// <summary>末尾没有分隔符就补一个，已经有了就原样返回；不改变路径的相对/绝对性。</summary>
    public static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
