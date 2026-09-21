using System.Globalization;
using System.Text.Json;

namespace LanMountainDesktop.Services.Json;

/// <summary>
/// 读第三方 JSON 响应时"按路径取值"的公共口径。此前 <c>HolidayCalendarService</c>、
/// <c>RecommendationDataService</c>、<c>XiaomiWeatherService</c> 各抄了一份私有实现
/// （<c>TryGetNode</c> 3 份同体、<c>ReadString</c> 3 份同体、<c>ReadInt</c> 2 份同体、
/// <c>ReadBool</c> 2 份同体），同一份云端返回在三个服务里可能被读成不一样的值。
///
/// 宽松读法是刻意的：天气/节假日/推荐这三家接口的字段类型并不严格一致（同一个数值字段有的给数字有的给字符串），
/// 所以数字/布尔都接受字符串形式，读不出来返回 null 而不是抛。
/// </summary>
public static class JsonNodeReader
{
    public static JsonElement? TryGetNode(JsonElement node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    public static string? ReadString(JsonElement? node, params string[] path)
    {
        var target = Resolve(node, path);
        if (target is null)
        {
            return null;
        }

        return target.Value.ValueKind switch
        {
            JsonValueKind.String => target.Value.GetString(),
            JsonValueKind.Number => target.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static int? ReadInt(JsonElement? node, params string[] path)
    {
        var target = Resolve(node, path);
        if (target is null)
        {
            return null;
        }

        if (target.Value.ValueKind == JsonValueKind.Number && target.Value.TryGetInt32(out var number))
        {
            return number;
        }

        if (target.Value.ValueKind == JsonValueKind.String &&
            int.TryParse(target.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static double? ReadDouble(JsonElement? node, params string[] path)
    {
        var target = Resolve(node, path);
        if (target is null)
        {
            return null;
        }

        if (target.Value.ValueKind == JsonValueKind.Number && target.Value.TryGetDouble(out var number))
        {
            return number;
        }

        if (target.Value.ValueKind == JsonValueKind.String &&
            double.TryParse(target.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static bool? ReadBool(JsonElement? node, params string[] path)
    {
        var target = Resolve(node, path);
        if (target is null)
        {
            return null;
        }

        if (target.Value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (target.Value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (target.Value.ValueKind == JsonValueKind.Number && target.Value.TryGetInt32(out var number))
        {
            return number != 0;
        }

        if (target.Value.ValueKind == JsonValueKind.String)
        {
            var value = target.Value.GetString();
            if (bool.TryParse(value, out var parsedBool))
            {
                return parsedBool;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
            {
                return parsedInt != 0;
            }
        }

        return null;
    }

    private static JsonElement? Resolve(JsonElement? node, string[] path)
    {
        if (node is null)
        {
            return null;
        }

        return path.Length == 0 ? node : TryGetNode(node.Value, path);
    }
}
