using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using LanMountainDesktop.AirAppSdk;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// AirAppSdk 公开表面的快照守卫。
///
/// 起因（可复现）：AirAppComponentOptions.ComponentId 曾在同一个 1.0.0 版本下从
/// <c>{ get; init; }</c> 改成 <c>{ get; set; }</c>。init 访问器带 IsExternalInit 标记，
/// 已编译的消费者按带 modreq 的签名去绑 set_ComponentId，运行时直接 MissingMethodException。
/// 于是 SamplePlugin 与 SchedulePlugin 的现成产物在今天的主机上根本加载不了 ——
/// 而包号还是 1.0.0，谁都看不出版本换过。
///
/// 这条测试把整个 SDK 的公开签名钉成一份文本基线：删成员、改名、换类型、init/set 互换，
/// 都会让基线出现 diff。改基线必须与版本号递增一起提交。
/// 已知它测不到的是：行为变更、以及仅靠签名无法区分的重载语义。
/// 还有一个更阴的测不到：基线是在那次破坏之后重新生成的，所以"相对已发布包变了"这件事它看不见
/// （061e405 一次改到 11 个属性 init→set 并删掉 IAirAppWorker/IAirAppWorkerContext 整族成员，
/// 包号仍是 1.0.0；基线是其后才录的，于是守卫一路绿，只有外部现成产物绑不上）。
/// 这一条目前只能靠 EcosystemProbe 那 8 行真加载兜住——它跑的就是外部仓库的现成二进制。
/// 台账（<c>AirAppSdk.PublicSurface.VersionLedger.txt</c>）补的就是这个洞的**下半截**：它管不了
/// 已经发生的那次，但让"同一个包号两份表面"从今往后当场红 —— 每次录基线都往台账追加一行
/// <c>版本号 + 表面哈希</c>，同一个版本号出现第二个哈希就拒绝追加，且 <c>LMD_UPDATE_AIRAPP_SDK_BASELINE</c>
/// 也绕不过去（它追加之前先撞同一条判据）。
/// </summary>
public sealed class AirAppSdkPublicSurfaceTests
{
    private const string BaselineRelativePath = "tests/LanMountainDesktop.Tests/ApprovalFiles/AirAppSdk.PublicSurface.txt";
    private const string VersionLedgerRelativePath = "tests/LanMountainDesktop.Tests/ApprovalFiles/AirAppSdk.PublicSurface.VersionLedger.txt";

    private static string BaselinePath => Path.Combine(RepoRoot, BaselineRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string VersionLedgerPath => Path.Combine(RepoRoot, VersionLedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void PublicSurface_MatchesCheckedInBaseline()
    {
        var current = RenderSurface();
        var isRecording = Environment.GetEnvironmentVariable("LMD_UPDATE_AIRAPP_SDK_BASELINE") is "1" or "true";

        // 台账先判：录制模式也绕不过"同一个包号换了一份表面"这条判据，
        // 否则一次破坏只要顺手重录基线就洗白了。
        EnforceVersionLedger(current, appendIfMissing: isRecording);

        if (isRecording)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
            File.WriteAllText(BaselinePath, current);
            return;
        }

        Assert.True(File.Exists(BaselinePath), $"缺少基线文件 {BaselineRelativePath}。设 LMD_UPDATE_AIRAPP_SDK_BASELINE=1 运行一次生成。");

        var approved = Normalize(File.ReadAllText(BaselinePath));
        if (approved != current)
        {
            var diff = DescribeDiff(approved.Split('\n'), current.Split('\n'));
            throw new Xunit.Sdk.XunitException(
                $"AirAppSdk 公开表面与基线不一致，但包号 {AirAppSdkInfo.SdkVersion} 在台账里只记着当前这一份表面"
                + $"（说明 {BaselineRelativePath} 被手改过，或表面变了却没走录制流程）："
                + $"必须同时递增 AirAppSdkInfo.SdkVersion / ApiVersion 与 Core 包版本，再更新 {BaselineRelativePath}。"
                + Environment.NewLine + diff);
        }
    }

    /// <summary>
    /// 台账判据：一个包号只能对应一份公开表面。
    /// 基线只比"相对上次录制变了没有"，所以"换了表面、没换包号"在它眼里是绿的 ——
    /// 而那恰好是外部 AirApp 绑不上的那一类破坏。
    /// </summary>
    private static void EnforceVersionLedger(string surface, bool appendIfMissing)
    {
        var version = AirAppSdkInfo.SdkVersion;
        var hash = SurfaceHash(surface);
        var recorded = ReadLedgerEntries().Where(entry => entry.Version == version).Select(entry => entry.Hash).ToArray();

        var clash = recorded.Where(known => known != hash).Distinct().ToArray();
        if (clash.Length > 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"包号 {version} 在 {VersionLedgerRelativePath} 里已经记着另一份公开表面（{string.Join("、", clash)}），当前是 {hash}。"
                + "同一个版本号只能有一份二进制表面：先递增 AirAppSdkInfo.SdkVersion / ApiVersion 与包版本，再重录基线。");
        }

        if (recorded.Length > 0)
        {
            return;
        }

        if (!appendIfMissing)
        {
            throw new Xunit.Sdk.XunitException(
                $"{VersionLedgerRelativePath} 里没有 ({version}, {hash}) 这一对。"
                + "递增过版本号或改过公开表面，都要设 LMD_UPDATE_AIRAPP_SDK_BASELINE=1 重录一次（同一次运行会追加台账）。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(VersionLedgerPath)!);
        File.AppendAllText(VersionLedgerPath, version + "  " + hash + Environment.NewLine);
    }

    private static (string Version, string Hash)[] ReadLedgerEntries()
    {
        if (!File.Exists(VersionLedgerPath))
        {
            return [];
        }

        return File.ReadAllLines(VersionLedgerPath)
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 2 && parts[0][0] != '#')
            .Select(parts => (parts[0], parts[1]))
            .ToArray();
    }

    private static string SurfaceHash(string surface) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(surface))))[..16].ToLowerInvariant();


    private static string RenderSurface()
    {
        var assembly = typeof(AirAppSdkInfo).Assembly;
        var lines = new List<string>();

        foreach (var type in assembly.GetExportedTypes())
        {
            lines.Add($"{TypeKind(type)} {FullName(type)}");

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var get = property.GetGetMethod();
                var set = property.GetSetMethod();
                lines.Add(Member(type, $"prop {property.Name} : {FullName(property.PropertyType)}"
                    + $" [{(get is null ? "-" : "get")}/{(set is null ? "-" : "set")}]"));
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName))
            {
                lines.Add(Member(type, $"method {FullName(method.ReturnType)} {method.Name}({ParameterSignature(method)})"));
            }

            foreach (var eventInfo in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                lines.Add(Member(type, $"event {eventInfo.Name} : {FullName(eventInfo.EventHandlerType!)}"));
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                lines.Add(Member(type, $"field {field.Name} : {FullName(field.FieldType)}"));
            }
        }

        // 全局按整行排序：反射返回顺序在不同进程间不保证一致，排完才谈得上稳定基线。
        lines.AddRange(RenderAccessorLines());
        lines.Sort(StringComparer.Ordinal);
        return Normalize(string.Join("\n", lines) + "\n");
    }

    /// <summary>
    /// 属性访问器的 get/set/init 形状必须从源码读：init 的信息载体是 setter 返回类型上的
    /// modreq(IsExternalInit)，反射会把它抹掉。而 init 与 set 互换正是二进制破坏
    /// —— 已编译消费者绑的是带 modreq 的 set_X，签名一变就 MissingMethodException。
    /// </summary>
    private static IEnumerable<string> RenderAccessorLines()
    {
        var sdkDirectory = Path.Combine(RepoRoot, "airapp", "LanMountainDesktop.AirAppSdk");
        var accessorPattern = new Regex(
            @"^\s*public\s+(?:(?:static|readonly|required|virtual|override|sealed|new|abstract)\s+)*"
            + @"([\w<>\[\]\?\.]+)\s+(\w+)\s*\{\s*(get;[^}]*?)\s*\}");
        var typePattern = new Regex(@"^\s*(?:public|internal)\s+(?:(?:static|sealed|abstract|partial|readonly|ref)\s+)*(?:class|struct|interface|enum|record)\s+(\w+)");

        foreach (var file in Directory.EnumerateFiles(sdkDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(file => file, StringComparer.Ordinal))
        {
            var owningType = Path.GetFileNameWithoutExtension(file);
            foreach (var rawLine in File.ReadAllLines(file))
            {
                var typeMatch = typePattern.Match(rawLine);
                if (typeMatch.Success)
                {
                    owningType = typeMatch.Groups[1].Value;
                }

                var match = accessorPattern.Match(rawLine);
                if (match.Success)
                {
                    yield return $"accessor {owningType}.{match.Groups[2].Value} : {match.Groups[1].Value} {{ {NormalizeAccessors(match.Groups[3].Value)} }}";
                }
            }
        }
    }

    private static string NormalizeAccessors(string accessors) =>
        string.Join(" ", accessors.Split([';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(token => token, StringComparer.Ordinal));

    private static string Member(Type type, string line) => $"{line}  @{FullName(type)}";

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string ParameterSignature(MethodInfo method) =>
        string.Join(", ", method.GetParameters().Select(parameter => FullName(parameter.ParameterType) + " " + parameter.Name));

    private static string TypeKind(Type type)
    {
        if (type.IsInterface) return "interface";
        if (type.IsEnum) return "enum";
        if (type.IsValueType) return "struct";
        if (type.IsAbstract && type.IsSealed) return "static-class";
        if (type.IsAbstract) return "abstract-class";
        if (type.IsSealed) return "sealed-class";
        return "class";
    }

    private static string FullName(Type type) => type.IsGenericType
        ? type.Name + "<" + string.Join(", ", type.GetGenericArguments().Select(FullName)) + ">"
        : (type.FullName ?? type.Name);

    private static string DescribeDiff(string[] approved, string[] current)
    {
        var removed = approved.Except(current).ToArray();
        var added = current.Except(approved).ToArray();
        var builder = new StringBuilder();
        if (removed.Length > 0)
        {
            builder.AppendLine("- 被移除：").AppendLine(string.Join(Environment.NewLine, removed.Take(40)));
        }

        if (added.Length > 0)
        {
            builder.AppendLine("+ 新增：").AppendLine(string.Join(Environment.NewLine, added.Take(40)));
        }

        return builder.ToString();
    }

    private static string RepoRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "LanMountainDesktop.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Unable to locate repository root.");
        }
    }
}
