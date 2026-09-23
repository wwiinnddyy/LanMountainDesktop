using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

/// <summary>
/// 把一扇 WinRT 异步操作等成结果值的唯一写法：认不出结果类型 / 拿不到 Task / 没有 Result 属性，
/// 一律回 <c>null</c>；失败与取消照原样抛给调用方——那正是三个调用方各自的 catch 在等的东西
/// （<c>LocationService</c> 靠 HRESULT 分类失败原因，吞掉异常就等于把"被拒绝"看成"没定位到"）。
///
/// 此前 <c>LocationService</c> 与 <c>WindowsSmtcMusicControlService</c> 各抄一份逐字相同的实现，
/// 两份一起改调这里。<c>WindowsNotificationListener</c> **没并进来**：它在方法内部另有一句
/// <c>ConfigureAwait(false)</c>（部分调用点在外面还再配一次），续接线程与这两家不同——
/// 那是行为差别，不在这笔里替它三挑，已登记成待拍板项（#G1-BC）。
///
/// <paramref name="asTaskGenericMethodDefinition"/> 由调用方给：<c>AsTask</c> 扩展方法的取法
/// 三家本来就不一样（一处带 try/catch 与形参校验，另两处用 LINQ 挑），那条差别同样没抹平。
/// </summary>
internal static class WinRtAsyncAwait
{
    public static async Task<object?> AwaitAsync(
        object? operation,
        MethodInfo? asTaskGenericMethodDefinition,
        CancellationToken cancellationToken)
    {
        if (operation is null || asTaskGenericMethodDefinition is null)
        {
            return null;
        }

        var resultType = WinRtOperationResult.ResolveType(operation.GetType());
        if (resultType is null)
        {
            return null;
        }

        var asTaskMethod = asTaskGenericMethodDefinition.MakeGenericMethod(resultType);
        if (asTaskMethod.Invoke(null, [operation]) is not Task taskObject)
        {
            return null;
        }

        await taskObject.WaitAsync(cancellationToken);
        return taskObject
            .GetType()
            .GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(taskObject);
    }
}
