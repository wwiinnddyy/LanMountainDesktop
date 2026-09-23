using System;
using System.Reflection;

namespace LanMountainDesktop.Services;

/// <summary>
/// "到 WinRT 投影里按名字取一个类型 / 方法"这件事只认这一处，两个装配标识的写法也只认这一处。
///
/// 收成一家的是**拼法**，不是判定：认不出来一律给 <c>null</c>、不抛，调用方判据是"0 就跳过这次能力"。
/// 理由是这条路全程没有异常——定位、通知、播放状态三处都是"拿不到就当本机没这个能力"，
/// 装配名拼错一个字母的症状是那个能力静默消失，而不是报错。
/// 按 <c>FullName</c> + 装配名字符串认（而不是 <c>typeof(...)</c>）是刻意的：宿主不在 WinRT 投影里编译，
/// 拿不到那个 CLR 类型。
///
/// 故意没接管的是 <c>AsTask</c> 的泛型方法定义怎么挑：三个服务各写了一套判据
/// （一家带 try/catch 与形参校验、另两家 LINQ 挑第一个），那是 #G1-BC 上等拍板的差别，
/// 这里只把它们的装配名换成 <see cref="ResolveProjectionType"/>。
/// </summary>
internal static class WinRtReflection
{
    /// <summary>托管投影程序集名（<c>System.WindowsRuntimeSystemExtensions</c> 与
    /// <c>System.IO.WindowsRuntimeStreamExtensions</c> 都住在里面）。</summary>
    public const string ProjectionAssemblyName = "System.Runtime.WindowsRuntime";

    /// <summary>WinRT 类型自己的装配限定（不是普通程序集名，ContentType 是必需的一段）。</summary>
    public const string WinRtTypeQualifier = "Windows, ContentType=WindowsRuntime";

    /// <summary>取托管投影里的类型；取不到给 <c>null</c>。</summary>
    public static Type? ResolveProjectionType(string typeFullName) =>
        Type.GetType(typeFullName + ", " + ProjectionAssemblyName, throwOnError: false);

    /// <summary>取 WinRT 运行时里的类型（<c>Windows.*</c>）；取不到给 <c>null</c>。</summary>
    public static Type? ResolveWinRtType(string typeName) =>
        Type.GetType(typeName + ", " + WinRtTypeQualifier, throwOnError: false);

    /// <summary>在已经拿到的类型里找一个 public static、恰好一个参数的方法；找不到给 <c>null</c>。</summary>
    public static MethodInfo? ResolveStaticMethod(Type? type, string methodName)
    {
        if (type is null)
        {
            return null;
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name == methodName && method.GetParameters().Length == 1)
            {
                return method;
            }
        }

        return null;
    }
}
