using System;

namespace LanMountainDesktop.Shared.Text;

/// <summary>
/// "一串候选里取第一个真有内容的"——唯一一份实现。
///
/// 此前 <c>LauncherRuntimeMetadata</c>（启动器读部署目录/可执行文件名）与宿主的
/// <c>PlondsManifestParser</c>（解析更新清单的显示名与动作）各抄了一份逐字相同的 8 行。
/// 两份算的是同一件事，却分在两个二进制里：判据漂开的症状不是崩，
/// 而是同一份清单在启动器与宿主眼里"哪个字段算有值"不一致——
/// 一边认定空白串要跳过、另一边把它当成有效值，于是标题显示成空白或干脆丢了回退。
///
/// 认定"有内容"的标准是 <c>!IsNullOrWhiteSpace</c>：只含空格的串算没有值。
/// 这一格是这条判据的全部要点，所以把它钉在测试里（<c>TextValueTests</c>）。
/// </summary>
public static class TextValue
{
    /// <summary>按给定顺序返回第一个非空白候选；全是空白或一个都没给，回 <c>null</c>。</summary>
    public static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
