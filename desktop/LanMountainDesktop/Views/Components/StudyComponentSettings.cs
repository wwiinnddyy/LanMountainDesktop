using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 学习组件那对"跟着设置走"的状态：当前界面语言 + 学习监测开关。
///
/// 两件事钉在这里：
/// ① 两个字段必须来自**同一份**设置快照。分两次读盘会出现"语言已经是新的、开关还是旧的"，
///    表现是切语言后组件按上一次的监测设置决定要不要持有租约。
/// ② 语言归一化只走 <see cref="LocalizationService.ResolveLanguageCode"/> 这个家。
///    此前 7 个学习组件各抄了一份逐字相同的三行体，抄的是 <c>NormalizeLanguageCode</c>——
///    绕开家本身没有行为差别（家就是 try { Normalize(读) } catch { 默认语言 }），
///    所以这里把 <c>settings.Load()</c> 留在 try 之外，读盘失败仍旧冒到调用方，与改前一致。
///    归一化口径以后若要变（例如新增语言），只需要改一处。
/// </summary>
internal static class StudyComponentSettings
{
    internal static void Reload(
        ref string languageCode,
        ref bool studyEnabled,
        LanMountainDesktop.AirAppSdk.ISettingsService settings,
        LocalizationService localization)
    {
        var snapshot = settings.Load();
        languageCode = localization.ResolveLanguageCode(() => snapshot.LanguageCode);
        studyEnabled = snapshot.StudyEnabled;
    }
}
