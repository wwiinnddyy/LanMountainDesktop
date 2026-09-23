namespace LanMountainDesktop.Shared.IO;

/// <summary>
/// "这个路径是不是那棵树里（或就是那棵树）"只认这一处。
///
/// 收口前三份抄本散在两个二进制里：<c>InstallerPathGuard.IsSameOrChildPath</c>（安装器，公开）、
/// <c>PlondsPackageStore.IsSameOrChildPath</c>（宿主，私有）、<c>PlondsPreparedPackageInstaller.EnsureChildPath</c>
/// （宿主，判定内联在抛异常那个外壳里、三个条件换了顺序）。三份压掉排版后逐字相同，
/// 换写法的那份此前两头隐身——这条判据一旦漂开，症状是"包外的路径被当成包内"，不报错。
///
/// 口径两条，都按抄本原样搬过来，没有顺手改：
/// ① 两侧都先 <c>Path.GetFullPath</c> 绝对化（会抛 <c>ArgumentException</c>，调用方要能接）；
///    末尾分隔符在比较前抹掉，所以 <c>C:\pkg</c> 与 <c>C:\pkg\</c> 算同一棵树。
/// ② 大小写按 <c>OrdinalIgnoreCase</c>：Windows 上对，在大小写敏感的文件系统上会放宽
///    （<c>/app/PKG</c> 会被认成 <c>/app/pkg</c> 的子树）。本仓当前只出 Windows 包，
///    这一条是**已登记待拍板**的问题，不是这里改得出的策略——改它要连同 #G1-BI 一起判。
/// </summary>
public static class PathContainment
{
    public static bool IsSameOrChild(string parent, string child)
    {
        var resolvedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolvedChild = Path.GetFullPath(child);
        return string.Equals(
                   resolvedParent,
                   resolvedChild.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase)
               || resolvedChild.StartsWith(resolvedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || resolvedChild.StartsWith(resolvedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
