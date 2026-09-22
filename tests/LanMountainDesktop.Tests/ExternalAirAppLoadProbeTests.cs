using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

using LanMountainDesktop.AirApps;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 真加载探针：把外部 AirApp 仓库现成的构建输出喂给宿主自己的 AirAppLoader。
///
/// 为什么需要：全仓此前没有任何测试构造过 AirAppLoader（真实加载覆盖为 0），而加载链路有两个
/// 静默点 —— LoadAll 在根目录不存在时返回空列表（AirAppLoader.cs:40-43），组件是从 DI 里
/// GetServices 取（AirAppLoader.cs:184-188），取空不抛。也就是说"清单声明了组件、代码里没注册"
/// 这类问题今天完全测不出来，装上≠能用。
///
/// 缺同级仓库或没构建时动态 Skip，免得把 CI 变成必然红灯；要在本地强制全覆盖，设
/// LMD_STRICT_AIRAPP_PROBE=1 跑 Coverage_IsNotSilentlyEmpty。
///
/// 这一类跑的是外部仓库的现成产物，结果不属于本仓库的可控范围，所以单独打 Category：
///   回归闸门（默认套件）：dotnet test --filter "Category!=EcosystemProbe"
///   生态体检：            dotnet test --filter "Category=EcosystemProbe"
/// </summary>
[Trait("Category", "EcosystemProbe")]
[Collection("AppDataPath")]
public sealed class ExternalAirAppLoadProbeTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(ExternalAirAppLoadProbeTests),
        Guid.NewGuid().ToString("N"));

    private ProbeHostServiceProvider? _hostServices;

    private IServiceProvider HostServices() => _hostServices ??= new ProbeHostServiceProvider(_dataRoot);

    public void Dispose()
    {
        _hostServices?.Dispose();
        AppDataPathProvider.ResetForTests();
        try
        {
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static readonly string[] AirAppRepos =
    [
        "LanMountainDesktop.SamplePlugin",
        "LanMountainDesktop.SchedulePlugin",
        "Classworks4LanDesktop",
        "LanDesktopHot",
        "LanWordPluginRepo",
        "VoiceHubLanDesktop"
    ];

    [Theory]
    [MemberData(nameof(AirAppRepoNames))]
    public void BuiltAirApp_LoadsAndRegistersEveryDeclaredComponent(string repoName)
    {
        var manifestPath = RequireBuiltManifest(repoName);
        var loader = CreateLoaderFor(manifestPath);

        var result = loader.LoadFromManifest(manifestPath, HostServices());

        if (!result.IsSuccess)
        {
            RequireActionableError(repoName, result);
        }

        Assert.True(
            result.IsSuccess,
            $"{repoName} 加载失败：{Describe(result.Error)}（清单 {manifestPath}）");

        using (result.LoadedAirApp!)
        {
            var declared = result.Manifest!.Components ?? [];
            var registered = result.LoadedAirApp!.DesktopComponents
                .Select(component => component.ComponentId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = declared
                .Select(component => component.Id)
                .Where(id => !registered.Contains(id))
                .ToArray();

            Assert.True(
                missing.Length == 0,
                $"{repoName} 清单声明了 {declared.Count} 个组件，DI 里缺：{string.Join(", ", missing)}");

            // 声明与注册的反向也要成立：注册了却没声明的组件不会出现在添加面板里。
            var undeclared = registered
                .Where(id => !declared.Any(component => string.Equals(component.Id, id, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            Assert.True(
                undeclared.Length == 0,
                $"{repoName} 注册了清单里没有的组件（用户拿不到）：{string.Join(", ", undeclared)}");
        }
    }

    [Fact]
    public void LegacyPluginManifest_IsInvisibleToDiscovery_NotSilentlyHalfLoaded()
    {
        // elysia.LanDesktopConnect 仍是 plugin.json + apiVersion 5.0.0。宿只扫 airapp.json，
        // 所以它的真实症状不是"报错"，而是装上后根本不出现在列表里 —— 用户只会看到"装了没反应"。
        var repoDirectory = Path.Combine(SiblingRoot, "elysia.LanDesktopConnect");
        if (!Directory.Exists(repoDirectory))
        {
            RequireStrictProbeOrSkip($"缺少同级仓库 {repoDirectory}");
            return;
        }

        var legacyManifest = Directory
            .EnumerateFiles(repoDirectory, "plugin.json", SearchOption.AllDirectories)
            .FirstOrDefault(file => !file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        Assert.NotNull(legacyManifest);

        var staging = Path.Combine(Path.GetTempPath(), "lmd-probe-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            File.Copy(legacyManifest!, Path.Combine(staging, "plugin.json"));

            var results = new AirAppLoader().LoadAll(staging);

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void Coverage_IsNotSilentlyEmpty()
    {
        var built = AirAppRepos
            .Select(repo => (repo, manifest: TryFindBuiltManifest(repo)))
            .ToArray();
        var usable = built.Where(pair => pair.manifest is not null).ToArray();

        Assert.True(
            usable.Length == AirAppRepos.Length,
            "只有 " + usable.Length + "/" + AirAppRepos.Length + " 个外部 AirApp 有可用构建输出："
                + string.Join(", ", built.Where(pair => pair.manifest is null).Select(pair => pair.repo)));
    }

    public static TheoryData<string> AirAppRepoNames { get; } = BuildRepoNames();

    private static TheoryData<string> BuildRepoNames()
    {
        var data = new TheoryData<string>();
        foreach (var repo in AirAppRepos)
        {
            data.Add(repo);
        }

        return data;
    }

    /// <summary>
    /// 绑定类失败必须被宿主翻译成人话（AirAppLoader.ExplainBindingFailure）。
    /// 裸的 "Method not found: 'Void ...set_ComponentId(String)'" 对作者和用户都是死路。
    /// </summary>
    private static void RequireActionableError(string repoName, AirAppLoadResult result)
    {
        if (result.Error?.InnerException is not (MissingMethodException or MissingMemberException or TypeLoadException))
        {
            return;
        }

        Assert.Contains("二进制不兼容", result.Error.Message);
        Assert.Contains("重新构建", result.Error.Message);
        Assert.Contains(AirAppSdkInfo.SdkVersion, result.Error.Message);
    }

    private static string Describe(Exception? error) => error switch
    {
        null => "(无异常)",
        // 这三类都是"产物比本仓库的 SDK 表面旧/新"，不是清单或宿主逻辑问题：
        // 要么该仓库需要用当前 SDK 重新构建，要么 SDK 在同一个版本号下改了公开签名。
        MissingMethodException or MissingMemberException or TypeLoadException =>
            $"{error.GetType().Name}: {error.Message} —— 该产物编译时用的 SDK 表面与本仓库当前源码不一致，"
            + "需要重新构建该 AirApp，或 SDK 必须递增版本号（同版本号改公开签名就是二进制破坏）"
            + InnerChain(error),
        _ => $"{error.GetType().Name}: {error.Message}{InnerChain(error)}"
    };

    /// <summary>
    /// 宿主把真实原因包在 InnerException 里（<c>AirAppLoader.cs:246-253</c> 那句"原始错误见内部异常"），
    /// 探针不展开就只剩一句结论、说不出**是哪个成员**断了——而要判"能不能不改版本号就修好"，
    /// 需要的正是那个成员签名。所以这里把整条内部链都打出来。
    /// </summary>
    private static string InnerChain(Exception error)
    {
        var builder = new StringBuilder();
        for (var inner = error.InnerException; inner is not null; inner = inner.InnerException)
        {
            builder.Append(Environment.NewLine)
                .Append("  ← ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 复刻宿主加载前的准备：AirAppRuntimeService 先用 AirAppSharedContractManager 把
    /// manifest.sharedContracts 里的程序集装进默认上下文（AirAppRuntimeService.cs:393、:856-860），
    /// 再把简单名加进 SharedAssemblyNames 交给 loader。探针不联网、也不写用户数据目录，
    /// 所以契约直接取本地市场仓库的同一份文件（LanAirApp/airappmarket/contracts/&lt;id&gt;/&lt;version&gt;/）。
    /// </summary>
    private static AirAppLoader CreateLoaderFor(string manifestPath)
    {
        var options = new AirAppLoaderOptions { IsDevMode = true };

        foreach (var contract in ReadSharedContracts(manifestPath))
        {
            var local = Path.Combine(
                SiblingRoot, "LanAirApp", "airappmarket", "contracts", contract.Id, contract.Version, contract.AssemblyFileName);

            if (!File.Exists(local))
            {
                RequireStrictProbeOrSkip(
                    $"{manifestPath} 声明的共享契约 {contract.Id} {contract.Version} 本地没有，宿主会走市场下载，探针不联网");
                continue;
            }

            options.SharedAssemblyNames.Add(Assembly.LoadFrom(local).GetName().Name!);
        }

        return new AirAppLoader(options);
    }

    private static IReadOnlyList<(string Id, string Version, string AssemblyFileName)> ReadSharedContracts(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("sharedContracts", out var contracts)
            || contracts.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<(string, string, string)>();
        }

        return contracts.EnumerateArray()
            .Select(element => (
                Id: GetString(element, "id"),
                Version: GetString(element, "version"),
                AssemblyFileName: GetString(element, "assemblyName")))
            .Where(contract => contract.Id.Length > 0 && contract.AssemblyFileName.Length > 0)
            .ToArray();

        static string GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    private static string RequireBuiltManifest(string repoName)
    {
        var manifest = TryFindBuiltManifest(repoName);
        if (manifest is null)
        {
            RequireStrictProbeOrSkip($"{repoName} 没有可加载的构建输出（airapp.json 旁找不到 entranceAssembly）");
        }

        return manifest!;
    }

    private static void RequireStrictProbeOrSkip(string reason)
    {
        if (Environment.GetEnvironmentVariable("LMD_STRICT_AIRAPP_PROBE") is "1" or "true")
        {
            throw new Xunit.Sdk.XunitException("严格模式：" + reason);
        }

        Assert.Skip(reason);
    }

    private static string? TryFindBuiltManifest(string repoName)
    {
        var repoRoot = Path.Combine(SiblingRoot, repoName);
        if (!Directory.Exists(repoRoot))
        {
            return null;
        }

        var binRoot = Path.Combine(repoRoot, "bin");
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(binRoot, AirAppSdkInfo.ManifestFileName, SearchOption.AllDirectories)
            .Where(HasEntranceAssemblyBeside)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool HasEntranceAssemblyBeside(string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("entranceAssembly", out var entrance))
            {
                return false;
            }

            return File.Exists(Path.Combine(Path.GetDirectoryName(manifestPath)!, entrance.GetString() ?? string.Empty));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 探针必须给 AirApp 一张和生产一致的服务图。宿主在 <c>AirAppLoader.CreateServiceCollection</c>
    /// 里把宿主服务转注进每个 AirApp 的容器（AirAppLoader.cs:358-366），真实提供方是
    /// <c>AirAppRuntimeService.AirAppHostServiceProvider</c>（:1105-1186）。
    /// 之前探针传 null，于是任何用 <c>ISettingsService</c> 的插件都报"No constructor can be instantiated"
    /// ——那是探针失真造出来的假红灯，插件在生产里是能解析的。
    /// 这里只给**能在无 UI 环境下拿到真实现**的那几个；拿不到的仍返回 null，
    /// 那样插件解析失败就是一条指名道姓的真红灯。
    /// </summary>
    private sealed class ProbeHostServiceProvider : IServiceProvider, IDisposable
    {
        private readonly SettingsFacadeService _facade;
        private readonly SettingsService _settings;
        private readonly SettingsCatalogService _catalog;
        private readonly IMaterialColorService _materialColors;
        private readonly IAppearanceThemeService _appearance;

        public ProbeHostServiceProvider(string dataRoot)
        {
            AppDataPathProvider.Initialize(["--data-root", dataRoot]);
            _settings = new SettingsService();
            _catalog = new SettingsCatalogService();
            _facade = new SettingsFacadeService();
            _materialColors = HostMaterialColorProvider.GetOrCreate();
            _appearance = HostAppearanceThemeProvider.GetOrCreate();
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ISettingsService))
            {
                return _settings;
            }

            if (serviceType == typeof(ISettingsCatalog))
            {
                return _catalog;
            }

            if (serviceType == typeof(ISettingsFacadeService))
            {
                return _facade;
            }

            if (serviceType == typeof(IMaterialColorService))
            {
                return _materialColors;
            }

            if (serviceType == typeof(IAppearanceThemeService))
            {
                return _appearance;
            }

            return null;
        }

        public void Dispose() => _facade.Dispose();
    }

    private static string SiblingRoot =>
        Directory.Exists(Path.Combine(ParentOfRepoRoot, "LanAirApp"))
            ? ParentOfRepoRoot
            : throw new InvalidOperationException($"未找到 AirApp 同级仓库根：{ParentOfRepoRoot}");

    private static string ParentOfRepoRoot => Directory.GetParent(RepoRoot)!.FullName;

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
