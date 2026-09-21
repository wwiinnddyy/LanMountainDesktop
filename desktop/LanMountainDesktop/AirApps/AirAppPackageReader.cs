using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.AirApps;

/// <summary>
/// 从 AirApp 包里读清单，全宿主只这一处。此前 <c>AirAppLoader</c>、市场安装、运行时服务、
/// 待升级队列各抄了一份"开 zip → 找 airapp.json → <see cref="AirAppManifest.Load"/>"，
/// 四份唯一实质差别是要不要先把路径规范化、文件不存在时给谁的错误。
/// 抄四份的代价是：清单契约改了（比如新增必填入参）得记着改四处，漏掉的那条路径会在
/// 用户机器上以另一种方式失败。
/// </summary>
internal static class AirAppPackageReader
{
    public static AirAppManifest ReadManifest(
        string packagePath,
        string manifestFileName = AirAppSdkInfo.ManifestFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new FileNotFoundException($"AirApp package '{fullPackagePath}' was not found.", fullPackagePath);
        }

        using var archive = ZipFile.OpenRead(fullPackagePath);
        var manifestEntries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) &&
                           string.Equals(entry.Name, manifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (manifestEntries.Length == 0)
        {
            throw new InvalidOperationException(
                $"AirApp package '{fullPackagePath}' does not contain '{manifestFileName}'.");
        }

        if (manifestEntries.Length > 1)
        {
            throw new InvalidOperationException(
                $"AirApp package '{fullPackagePath}' contains multiple '{manifestFileName}' files.");
        }

        using var stream = manifestEntries[0].Open();
        return AirAppManifest.Load(stream, $"{fullPackagePath}!/{manifestEntries[0].FullName}");
    }
}
