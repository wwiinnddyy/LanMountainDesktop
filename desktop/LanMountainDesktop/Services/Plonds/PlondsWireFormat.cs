namespace LanMountainDesktop.Services.Plonds;

/// <summary>
/// 宿主侧的 PLONDS 线上协议字面量，唯一真源。服务端对应
/// PenguinLogisticsOnlineNetworkDistributionSystem/src/Plonds.Shared/PlondsConstants.cs，
/// 两个程序之间没有共享类型，只能靠字符串巧合对齐 —— 所以宿主这边绝不允许再出现第二处写法。
/// 契约本身由 tests/LanMountainDesktop.Tests/PlondsDistributionContractTests.cs 拿服务端样例元数据钉住，
/// 字面量不得散落的守卫在 SourceIntegrityTests 里。
/// </summary>
internal static class PlondsWireFormat
{
    /// <summary>全量包文件名（工具侧约定大小写）。</summary>
    public const string FullPackageFileName = "Files.zip";

    /// <summary>历史上出现过的全量包小写名，下载时要两种都试。</summary>
    public const string FullPackageFileNameLowerCase = "files.zip";

    /// <summary>更早的 windows-x64 专用包名，兼容旧快照。</summary>
    public const string LegacyWindowsX64PackageFileName = "files-windows-x64.zip";

    /// <summary>增量包文件名。</summary>
    public const string DeltaPackageFileName = "changed.zip";

    /// <summary>commit-delta 清单文件名。</summary>
    public const string CommitDeltaManifestFileName = "PLONDS.json";

    public const string ActionAdd = "add";
    public const string ActionReplace = "replace";
    public const string ActionReuse = "reuse";
    public const string ActionDelete = "delete";

    public const string HashAlgorithmMd5 = "md5";
    public const string HashAlgorithmSha256 = "sha256";
    public const string HashAlgorithmSha512 = "sha512";
}
