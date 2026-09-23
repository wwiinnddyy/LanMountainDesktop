namespace Windows.Foundation
{
    /// <summary>与 WinRT 投影里的同名接口按 FullName 对齐——判据认的就是这个名字。</summary>
    public interface IAsyncOperation<out T>
    {
    }
}

namespace Other.Foundation
{
    /// <summary>简单名相同、命名空间不同的干扰项。</summary>
    public interface IAsyncOperation<out T>
    {
    }
}

namespace LanMountainDesktop.Tests
{
    using System;
    using System.Collections.Generic;

    using LanMountainDesktop.Services;

    using Xunit;

    /// <summary>
    /// WinRT 操作结果类型那条判据的家唯一的证据。此前三个服务各抄一份（两份逐字相同、一份等价换写法），
    /// 抄歪的症状是"定位/通知/播放状态静默拿不到值"——调用方把 <c>null</c> 当成"这次不算"，全程不报错。
    ///
    /// 夹具用自造的 <c>Windows.Foundation.IAsyncOperation&lt;T&gt;</c>（就在上面声明）而不是真 WinRT 类型：
    /// 判据本来就是按 FullName 认接口的（宿主不在 WinRT 投影里编译），所以这么钉既离线可跑，
    /// 又正好把"按名字认"这件事本身钉住了。
    /// </summary>
    public sealed class WinRtOperationResultTests
    {
        [Fact]
        public void ResolveType_UnaryGenericOperation_UsesItsOwnArgument()
        {
            Assert.Equal(typeof(string), WinRtOperationResult.ResolveType(typeof(List<string>)));
        }

        [Fact]
        public void ResolveType_NonGenericImplementingTheNamedInterface_UsesItsArgument()
        {
            Assert.Equal(typeof(int), WinRtOperationResult.ResolveType(typeof(IntOperation)));
        }

        [Fact]
        public void ResolveType_GenericWithWrongArity_FallsThroughToTheInterfaceScan()
        {
            // 一元泛型那步必须真的判 arity。量过的对照：只判 IsGenericType 时这一格红（它会返回第一个实参
            // string），所以"不判 arity"这条错法在这里不是静默通过，而是返回一个 MakeGenericMethod 用不了的结果类型。
            Assert.Equal(typeof(long), WinRtOperationResult.ResolveType(typeof(TwoArgOperation<string, int>)));
        }

        [Fact]
        public void ResolveType_OwnArgumentWinsOverAnImplementedInterface()
        {
            // 钉住两趟的先后：操作类型自己是一元泛型时用它自己的实参，不去找它实现的 IAsyncOperation<int>。
            Assert.Equal(typeof(string), WinRtOperationResult.ResolveType(typeof(OwnAndInterface<string>)));
        }

        [Fact]
        public void ResolveType_RequiresTheWholeFullName_NotJustTheSimpleName()
        {
            // 名字对、命名空间不对：必须认不出。注入"把 FullName 换成 Name"时红的是 NonGenericImplementing
            // 与 WrongArity 两格（换成简单名就谁的接口都不匹配了），这一格钉的是反向——只按简单名比会把别的
            // 命名空间也认下来。三格合起来才是"按全名认"这条口径。
            Assert.Null(WinRtOperationResult.ResolveType(typeof(LookalikeIntOperation)));
        }

        [Fact]
        public void ResolveType_PlainType_IsNull()
        {
            Assert.Null(WinRtOperationResult.ResolveType(typeof(string)));
            Assert.Null(WinRtOperationResult.ResolveType(typeof(object)));
        }

        private sealed class IntOperation : Windows.Foundation.IAsyncOperation<int>
        {
        }

        private sealed class LookalikeIntOperation : Other.Foundation.IAsyncOperation<int>
        {
        }

        private sealed class TwoArgOperation<TFirst, TSecond> : Windows.Foundation.IAsyncOperation<long>
        {
        }

        private sealed class OwnAndInterface<TFirst> : Windows.Foundation.IAsyncOperation<int>
        {
        }
    }
}
