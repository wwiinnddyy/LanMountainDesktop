using System;
using System.Globalization;
using System.Linq;

namespace LanMountainDesktop.Shared.Contracts.Deployment;

/// <summary>
/// 轻量 SemVer 2.0 解析与比较（支持 0.8.5-beta.1 等预发布版本）。
/// 安装器与启动器统一使用本类型比较版本，禁止使用 System.Version 解析渠道版本号。
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public int Revision { get; }
    public string? Prerelease { get; }

    private SemanticVersion(int major, int minor, int patch, int revision, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Revision = revision;
        Prerelease = string.IsNullOrWhiteSpace(prerelease) ? null : prerelease;
    }

    public static bool TryParse(string? value, out SemanticVersion? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimStart('v', 'V');

        // 去掉 build metadata（+xxx）
        var plusIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            normalized = normalized[..plusIndex];
        }

        string? prerelease = null;
        var dashIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            prerelease = normalized[(dashIndex + 1)..];
            normalized = normalized[..dashIndex];
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < 0)
            {
                return false;
            }
        }

        parsed = new SemanticVersion(numbers[0], numbers[1], numbers[2], numbers[3], prerelease);
        return true;
    }

    public static SemanticVersion Parse(string value)
    {
        return TryParse(value, out var parsed)
            ? parsed!
            : throw new FormatException($"Invalid semantic version: {value}");
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        core = Revision.CompareTo(other.Revision);
        if (core != 0) return core;

        // SemVer: 无预发布 > 有预发布
        if (Prerelease is null && other.Prerelease is null) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var length = Math.Max(leftParts.Length, rightParts.Length);
        for (var i = 0; i < length; i++)
        {
            if (i >= leftParts.Length) return -1;
            if (i >= rightParts.Length) return 1;

            var l = leftParts[i];
            var r = rightParts[i];
            var lNumeric = l.All(char.IsAsciiDigit);
            var rNumeric = r.All(char.IsAsciiDigit);
            int result;
            if (lNumeric && rNumeric)
            {
                result = long.Parse(l, CultureInfo.InvariantCulture)
                    .CompareTo(long.Parse(r, CultureInfo.InvariantCulture));
            }
            else if (lNumeric)
            {
                result = -1;
            }
            else if (rNumeric)
            {
                result = 1;
            }
            else
            {
                result = string.CompareOrdinal(l, r);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Revision, Prerelease);

    public override string ToString()
    {
        var core = Revision > 0
            ? $"{Major}.{Minor}.{Patch}.{Revision}"
            : $"{Major}.{Minor}.{Patch}";
        return Prerelease is null ? core : $"{core}-{Prerelease}";
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
}
