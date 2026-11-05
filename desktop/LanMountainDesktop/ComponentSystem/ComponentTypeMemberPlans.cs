using System;
using System.Collections.Concurrent;
using System.Reflection;

using Avalonia.Threading;

using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.ComponentSystem;

/// <summary>
/// 组件类型成员的反射计划缓存：组件创建（设置依赖注入）与预览静默（停表）都要按实例的运行时类型
/// 全量枚举字段/属性。实测探针（2026-11-05）：一次设置注入形状的反射 10.2µs，而预览静默对
/// <b>每个控件</b>的反射 685.8µs——Avalonia 控件的属性系统很大，GetProperties+GetIndexParameters
/// 是大头；预览树几十上百个控件 × 每次 attach/detach 三轮，就是上百毫秒的纯 UI 线程反射。
/// 成员集合只取决于类型（flags 固定），按类型缓存一次、之后查表即得。
/// 计划的语义与原先的逐次枚举逐字一致：同样的 flags、同样的跳过规则（注入跳 initOnly /
/// 只读属性，停表跳索引器），错一个就会出现"某成员突然不被注入/不被停表"的静默漂移。
/// </summary>
internal static class ComponentTypeMemberPlans
{
    private static readonly BindingFlags InstanceMemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly ConcurrentDictionary<Type, TimerMemberPlan> TimerPlans = new();
    private static readonly ConcurrentDictionary<Type, SettingsInjectionPlan> SettingsInjectionPlans = new();

    public readonly record struct TimerMemberPlan(FieldInfo[] TimerFields, PropertyInfo[] TimerProperties);

    public readonly record struct SettingsInjectionPlan(
        FieldInfo[] SettingsServiceFields,
        FieldInfo[] ComponentStoreFields,
        PropertyInfo[] SettingsServiceProperties,
        PropertyInfo[] ComponentStoreProperties);

    public static TimerMemberPlan GetTimerPlan(Type type) =>
        TimerPlans.GetOrAdd(type, BuildTimerPlan);

    public static SettingsInjectionPlan GetSettingsInjectionPlan(Type type) =>
        SettingsInjectionPlans.GetOrAdd(type, BuildSettingsInjectionPlan);

    private static TimerMemberPlan BuildTimerPlan(Type type)
    {
        var timerFields = Array.FindAll(
            type.GetFields(InstanceMemberFlags),
            field => typeof(DispatcherTimer).IsAssignableFrom(field.FieldType));

        var timerProperties = Array.FindAll(
            type.GetProperties(InstanceMemberFlags),
            property => property.CanRead &&
                        property.GetIndexParameters().Length == 0 &&
                        typeof(DispatcherTimer).IsAssignableFrom(property.PropertyType));

        return new TimerMemberPlan(timerFields, timerProperties);
    }

    private static SettingsInjectionPlan BuildSettingsInjectionPlan(Type type)
    {
        var fields = type.GetFields(InstanceMemberFlags);
        var settingsServiceFields = Array.FindAll(
            fields,
            field => !field.IsInitOnly && IsSettingsServiceType(field.FieldType));
        var componentStoreFields = Array.FindAll(
            fields,
            field => !field.IsInitOnly && IsComponentStoreType(field.FieldType));

        var properties = type.GetProperties(InstanceMemberFlags);
        var settingsServiceProperties = Array.FindAll(
            properties,
            property => property.CanWrite && IsSettingsServiceType(property.PropertyType));
        var componentStoreProperties = Array.FindAll(
            properties,
            property => property.CanWrite && IsComponentStoreType(property.PropertyType));

        return new SettingsInjectionPlan(
            settingsServiceFields,
            componentStoreFields,
            settingsServiceProperties,
            componentStoreProperties);
    }

    private static bool IsSettingsServiceType(Type candidate) =>
        typeof(ISettingsService).IsAssignableFrom(candidate);

    private static bool IsComponentStoreType(Type candidate) =>
        typeof(IComponentInstanceSettingsStore).IsAssignableFrom(candidate);
}
