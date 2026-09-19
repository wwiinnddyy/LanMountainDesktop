using System.Reflection;
using dotnetCampus.Ipc.CompilerServices.Attributes;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// [IpcPublic] 契约只能暴露异步方法。
///
/// 原因不是风格：生成的同步代理内部用 Task.Wait() 等回包。一旦调用方正跑在 dotnetCampus 自己的
/// 读循环线程上（await ConnectAsync(...) 的延续、或在 notify handler 里直接调），回包就只能由这条
/// 线程投递，而它正被等待挡住 —— 必然死锁，表现是整个进程静默挂住而不是抛错。
/// ExternalIpcPublicApiTests 曾因同步调用 IPublicAppInfoService.GetAppInfo() 挂死整个 xUnit 子进程。
/// </summary>
public sealed class IpcPublicContractArchitectureTests
{
    /// <summary>
    /// 基线例外：这两个同步方法已随 LanMountainDesktop.Core 1.0.0 发布，属公开 API，
    /// 只能"加异步版本 + 标废弃"两步撤，不能直接改签名。只减不增，且第二个测试会盯住陈旧条目。
    /// </summary>
    private static readonly string[] SynchronousContractBaseline =
    [
        "LanMountainDesktop.Shared.IPC.Abstractions.Services.IPublicAppInfoService.GetAppInfo",
        "LanMountainDesktop.Shared.IPC.Abstractions.Services.IPublicAirAppCatalogService.GetCatalog",
    ];

    [Fact]
    public void IpcPublicContracts_DoNotExposeNewSynchronousMethods()
    {
        var offenders = SynchronousIpcPublicMethods()
            .Where(method => !SynchronousContractBaseline.Contains(method, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SynchronousContractBaseline_ContainsNoStaleEntries()
    {
        var present = SynchronousIpcPublicMethods().ToHashSet(StringComparer.Ordinal);

        var stale = SynchronousContractBaseline
            .Where(entry => !present.Contains(entry))
            .ToArray();

        Assert.Empty(stale);
    }

    private static IEnumerable<string> SynchronousIpcPublicMethods()
    {
        // 只扫这两个装配：AirApp 作者自己的 [IpcPublic] 契约在各自程序集里，静态扫不到，
        // 本守卫要拦的是我们发出去的公开契约上再长出同步方法。
        var assemblies = new[]
        {
            typeof(IPublicAppInfoService).Assembly,
            typeof(LanMountainDesktop.AirAppSdk.IAirApp).Assembly,
        };

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsInterface || !type.IsDefined(typeof(IpcPublicAttribute), inherit: false))
                {
                    continue;
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!IsAwaitable(method.ReturnType))
                    {
                        yield return $"{type.FullName}.{method.Name}";
                    }
                }
            }
        }
    }

    private static bool IsAwaitable(Type returnType)
    {
        // Task / Task<T>（Task<T> : Task），以及 ValueTask / ValueTask<T>。
        return typeof(Task).IsAssignableFrom(returnType) || typeof(ValueTask).IsAssignableFrom(returnType);
    }
}
