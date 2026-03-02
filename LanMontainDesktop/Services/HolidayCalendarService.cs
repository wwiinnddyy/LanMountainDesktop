using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LanMontainDesktop.Services;

public enum HolidayDayType
{
    Workday = 0,
    Weekend = 1,
    LegalHoliday = 2,
    AdjustedWorkday = 3,
    Unknown = 99
}

public sealed record HolidayDayStatus(
    DateOnly Date,
    HolidayDayType DayType,
    string TypeNameZh,
    bool IsHoliday,
    bool IsAdjustedWorkday,
    string? NameZh,
    string? NameEn,
    string? TargetHolidayZh);

public sealed record HolidayDisplayInfo(
    HolidayCountdownInfo? NextHoliday,
    HolidayDayStatus TodayStatus,
    bool UsesOnlineData);

public sealed class HolidayCalendarService : IDisposable
{
    private static readonly ChineseLunisolarCalendar LunarCalendar = new();

    private sealed record HolidayTemplate(
        string NameZh,
        string NameEn,
        Func<int, DateOnly?> ResolveDateForYear);

    private sealed record HolidayArrangementDay(
        DateOnly Date,
        bool IsHoliday,
        bool IsAdjustedWorkday,
        string NameZh,
        string NameEn,
        string? TargetHolidayZh,
        bool? IsAfterAdjust);

    private sealed record CacheEntry<T>(T Value, DateTimeOffset ExpireAt);

    private static readonly IReadOnlyDictionary<string, string> HolidayNameMapZhToEn =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["\u5143\u65e6"] = "New Year's Day",
            ["\u6625\u8282"] = "Spring Festival",
            ["\u6e05\u660e\u8282"] = "Tomb-Sweeping Day",
            ["\u52b3\u52a8\u8282"] = "Labor Day",
            ["\u7aef\u5348\u8282"] = "Dragon Boat Festival",
            ["\u4e2d\u79cb\u8282"] = "Mid-Autumn Festival",
            ["\u56fd\u5e86\u8282"] = "National Day",
            ["\u9664\u5915"] = "Lunar New Year's Eve",
            ["\u521d\u4e00"] = "Spring Festival Day 1",
            ["\u521d\u4e8c"] = "Spring Festival Day 2",
            ["\u521d\u4e09"] = "Spring Festival Day 3",
            ["\u521d\u56db"] = "Spring Festival Day 4",
            ["\u521d\u4e94"] = "Spring Festival Day 5",
            ["\u521d\u516d"] = "Spring Festival Day 6",
            ["\u521d\u4e03"] = "Spring Festival Day 7"
        };

    private static readonly IReadOnlyList<HolidayTemplate> HolidayTemplates =
    [
        new HolidayTemplate(
            "\u5143\u65e6",
            "New Year's Day",
            year => new DateOnly(year, 1, 1)),
        new HolidayTemplate(
            "\u6625\u8282",
            "Spring Festival",
            year => TryResolveLunarHolidayDate(year, lunarMonth: 1, lunarDay: 1)),
        new HolidayTemplate(
            "\u52b3\u52a8\u8282",
            "Labor Day",
            year => new DateOnly(year, 5, 1)),
        new HolidayTemplate(
            "\u7aef\u5348\u8282",
            "Dragon Boat Festival",
            year => TryResolveLunarHolidayDate(year, lunarMonth: 5, lunarDay: 5)),
        new HolidayTemplate(
            "\u4e2d\u79cb\u8282",
            "Mid-Autumn Festival",
            year => TryResolveLunarHolidayDate(year, lunarMonth: 8, lunarDay: 15)),
        new HolidayTemplate(
            "\u56fd\u5e86\u8282",
            "National Day",
            year => new DateOnly(year, 10, 1))
    ];

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;
    private readonly TimeSpan _yearCacheDuration;
    private readonly TimeSpan _dayCacheDuration;
    private readonly object _cacheGate = new();
    private readonly Dictionary<int, CacheEntry<IReadOnlyList<HolidayArrangementDay>>> _yearHolidayCache = new();
    private readonly Dictionary<DateOnly, CacheEntry<HolidayDayStatus>> _dayStatusCache = new();

    public HolidayCalendarService(
        HttpClient? httpClient = null,
        string baseUrl = "https://timor.tech",
        TimeSpan? yearCacheDuration = null,
        TimeSpan? dayCacheDuration = null,
        TimeSpan? requestTimeout = null)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://timor.tech" : baseUrl.Trim();
        _yearCacheDuration = yearCacheDuration ?? TimeSpan.FromHours(12);
        _dayCacheDuration = dayCacheDuration ?? TimeSpan.FromHours(3);

        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = requestTimeout ?? TimeSpan.FromSeconds(8)
            };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public async Task<HolidayDisplayInfo> GetDisplayInfoAsync(
        DateTime dateTime,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(dateTime.Date);
        var todayStatus = BuildFallbackDayStatus(today);
        var usesOnlineData = false;

        try
        {
            todayStatus = await GetDayStatusOnlineAsync(today, cancellationToken);
            usesOnlineData = true;
        }
        catch
        {
            // Keep local fallback status.
        }

        HolidayCountdownInfo? nextHoliday = null;
        try
        {
            nextHoliday = await GetNextHolidayOnlineAsync(today, cancellationToken);
            if (nextHoliday is not null)
            {
                usesOnlineData = true;
            }
        }
        catch
        {
            // Keep local fallback countdown.
        }

        nextHoliday ??= GetNextHoliday(dateTime);
        return new HolidayDisplayInfo(nextHoliday, todayStatus, usesOnlineData);
    }

    public HolidayCountdownInfo? GetNextHoliday(DateTime dateTime)
    {
        var today = DateOnly.FromDateTime(dateTime.Date);
        var candidates = BuildHolidayCandidates(today.Year - 1, today.Year + 3);
        HolidayCountdownInfo? best = null;

        foreach (var holiday in candidates)
        {
            if (holiday.Date < today)
            {
                continue;
            }

            if (best is null || holiday.Date < best.Date)
            {
                best = holiday;
            }
        }

        return best;
    }

    private async Task<HolidayCountdownInfo?> GetNextHolidayOnlineAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var candidates = new List<HolidayCountdownInfo>();
        for (var year = today.Year; year <= today.Year + 2; year++)
        {
            var yearData = await GetYearHolidayDataOnlineAsync(year, cancellationToken);
            foreach (var item in yearData)
            {
                if (!item.IsHoliday || item.Date < today)
                {
                    continue;
                }

                candidates.Add(new HolidayCountdownInfo(
                    NameZh: item.NameZh,
                    NameEn: item.NameEn,
                    Date: item.Date));
            }
        }

        return candidates.Count == 0
            ? null
            : candidates.OrderBy(item => item.Date).First();
    }

    private async Task<HolidayDayStatus> GetDayStatusOnlineAsync(DateOnly date, CancellationToken cancellationToken)
    {
        if (TryGetDayStatusFromCache(date, out var cached))
        {
            return cached;
        }

        var uri = BuildRequestUri($"/api/holiday/info/{date:yyyy-MM-dd}");
        var responseText = await FetchAsync(uri, cancellationToken);

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var code = ReadInt(root, "code");
        if (code.HasValue && code.Value != 0)
        {
            throw new InvalidOperationException($"Holiday API returned error code {code.Value}.");
        }

        var typeNode = TryGetNode(root, "type");
        var holidayNode = TryGetNode(root, "holiday");

        var apiType = ReadInt(typeNode, "type");
        var typeNameZh = ReadString(typeNode, "name") ?? string.Empty;
        var dayType = MapDayType(apiType);

        var holidayFlag = ReadBool(holidayNode, "holiday");
        var isHoliday = holidayFlag ?? dayType == HolidayDayType.LegalHoliday;
        var nameZh = ReadString(holidayNode, "name");
        var targetZh = ReadString(holidayNode, "target");
        var isAdjustedWorkday =
            dayType == HolidayDayType.AdjustedWorkday ||
            (!isHoliday &&
             (!string.IsNullOrWhiteSpace(targetZh) ||
              (nameZh?.Contains("\u8865\u73ed", StringComparison.Ordinal) ?? false)));

        var nameEn = ResolveHolidayEnName(nameZh, targetZh, isHoliday, isAdjustedWorkday);
        var result = new HolidayDayStatus(
            Date: date,
            DayType: dayType,
            TypeNameZh: typeNameZh,
            IsHoliday: isHoliday,
            IsAdjustedWorkday: isAdjustedWorkday,
            NameZh: nameZh,
            NameEn: nameEn,
            TargetHolidayZh: targetZh);

        SetDayStatusCache(date, result);
        return result;
    }

    private async Task<IReadOnlyList<HolidayArrangementDay>> GetYearHolidayDataOnlineAsync(int year, CancellationToken cancellationToken)
    {
        if (TryGetYearHolidayDataFromCache(year, out var cached))
        {
            return cached;
        }

        var uri = BuildRequestUri($"/api/holiday/year/{year}/");
        var responseText = await FetchAsync(uri, cancellationToken);

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var code = ReadInt(root, "code");
        if (code.HasValue && code.Value != 0)
        {
            throw new InvalidOperationException($"Holiday year API returned error code {code.Value}.");
        }

        var result = new List<HolidayArrangementDay>();
        var holidayRoot = TryGetNode(root, "holiday");
        if (holidayRoot is not null && holidayRoot.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in holidayRoot.Value.EnumerateObject())
            {
                var node = property.Value;
                var date = ParseDateOnly(ReadString(node, "date")) ??
                           ParseDateOnly(string.Create(CultureInfo.InvariantCulture, $"{year}-{property.Name}"));
                if (!date.HasValue)
                {
                    continue;
                }

                var isHoliday = ReadBool(node, "holiday") ?? false;
                var nameZh = ReadString(node, "name");
                if (string.IsNullOrWhiteSpace(nameZh))
                {
                    continue;
                }

                var targetZh = ReadString(node, "target");
                var isAfterAdjust = ReadBool(node, "after");
                var isAdjustedWorkday = !isHoliday &&
                                        (!string.IsNullOrWhiteSpace(targetZh) ||
                                         nameZh.Contains("\u8865\u73ed", StringComparison.Ordinal));

                result.Add(new HolidayArrangementDay(
                    Date: date.Value,
                    IsHoliday: isHoliday,
                    IsAdjustedWorkday: isAdjustedWorkday,
                    NameZh: nameZh,
                    NameEn: ResolveHolidayEnName(nameZh, targetZh, isHoliday, isAdjustedWorkday),
                    TargetHolidayZh: targetZh,
                    IsAfterAdjust: isAfterAdjust));
            }
        }

        result.Sort((left, right) => left.Date.CompareTo(right.Date));
        SetYearHolidayDataCache(year, result);
        return result;
    }

    private static List<HolidayCountdownInfo> BuildHolidayCandidates(int yearFrom, int yearToInclusive)
    {
        var results = new List<HolidayCountdownInfo>();
        if (yearToInclusive < yearFrom)
        {
            return results;
        }

        for (var year = yearFrom; year <= yearToInclusive; year++)
        {
            foreach (var template in HolidayTemplates)
            {
                var date = template.ResolveDateForYear(year);
                if (!date.HasValue)
                {
                    continue;
                }

                results.Add(new HolidayCountdownInfo(
                    NameZh: template.NameZh,
                    NameEn: template.NameEn,
                    Date: date.Value));
            }
        }

        results.Sort((left, right) => left.Date.CompareTo(right.Date));
        return results;
    }

    private static DateOnly? TryResolveLunarHolidayDate(int lunarYear, int lunarMonth, int lunarDay)
    {
        try
        {
            var mappedMonth = MapRegularLunarMonthToRawMonth(lunarYear, lunarMonth);
            var gregorian = LunarCalendar.ToDateTime(lunarYear, mappedMonth, lunarDay, 0, 0, 0, 0);
            return DateOnly.FromDateTime(gregorian);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int MapRegularLunarMonthToRawMonth(int lunarYear, int regularMonth)
    {
        var leapMonth = LunarCalendar.GetLeapMonth(lunarYear);
        if (leapMonth == 0)
        {
            return regularMonth;
        }

        // ChineseLunisolarCalendar month index inserts leap month:
        // if leap month is after regular month N, GetLeapMonth returns N + 1.
        // Months after that slot shift by +1.
        return leapMonth <= regularMonth ? regularMonth + 1 : regularMonth;
    }

    private static HolidayDayStatus BuildFallbackDayStatus(DateOnly date)
    {
        var dayOfWeek = date.DayOfWeek;
        var isWeekend = dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        return new HolidayDayStatus(
            Date: date,
            DayType: isWeekend ? HolidayDayType.Weekend : HolidayDayType.Workday,
            TypeNameZh: isWeekend ? "\u5468\u672b" : "\u5de5\u4f5c\u65e5",
            IsHoliday: false,
            IsAdjustedWorkday: false,
            NameZh: null,
            NameEn: null,
            TargetHolidayZh: null);
    }

    private static HolidayDayType MapDayType(int? apiType)
    {
        return apiType switch
        {
            0 => HolidayDayType.Workday,
            1 => HolidayDayType.Weekend,
            2 => HolidayDayType.LegalHoliday,
            3 => HolidayDayType.AdjustedWorkday,
            _ => HolidayDayType.Unknown
        };
    }

    private static string ResolveHolidayEnName(
        string? holidayNameZh,
        string? targetHolidayZh,
        bool isHoliday,
        bool isAdjustedWorkday)
    {
        if (!string.IsNullOrWhiteSpace(holidayNameZh) &&
            HolidayNameMapZhToEn.TryGetValue(holidayNameZh, out var holidayEn))
        {
            return holidayEn;
        }

        if (!string.IsNullOrWhiteSpace(targetHolidayZh) &&
            HolidayNameMapZhToEn.TryGetValue(targetHolidayZh, out var targetEn))
        {
            return isAdjustedWorkday
                ? $"{targetEn} Make-up Workday"
                : targetEn;
        }

        if (isAdjustedWorkday)
        {
            return "Make-up Workday";
        }

        return isHoliday ? "Holiday" : "Workday";
    }

    private async Task<string> FetchAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(content, 180)}");
        }

        return content;
    }

    private Uri BuildRequestUri(string path)
    {
        var baseUrl = _baseUrl.TrimEnd('/');
        var normalizedPath = path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";
        return new Uri($"{baseUrl}{normalizedPath}", UriKind.Absolute);
    }

    private bool TryGetYearHolidayDataFromCache(int year, out IReadOnlyList<HolidayArrangementDay> value)
    {
        lock (_cacheGate)
        {
            if (_yearHolidayCache.TryGetValue(year, out var entry) &&
                entry.ExpireAt > DateTimeOffset.UtcNow)
            {
                value = entry.Value;
                return true;
            }
        }

        value = Array.Empty<HolidayArrangementDay>();
        return false;
    }

    private void SetYearHolidayDataCache(int year, IReadOnlyList<HolidayArrangementDay> value)
    {
        lock (_cacheGate)
        {
            _yearHolidayCache[year] = new CacheEntry<IReadOnlyList<HolidayArrangementDay>>(
                value,
                DateTimeOffset.UtcNow.Add(_yearCacheDuration));
        }
    }

    private bool TryGetDayStatusFromCache(DateOnly date, out HolidayDayStatus value)
    {
        lock (_cacheGate)
        {
            if (_dayStatusCache.TryGetValue(date, out var entry) &&
                entry.ExpireAt > DateTimeOffset.UtcNow)
            {
                value = entry.Value;
                return true;
            }
        }

        value = BuildFallbackDayStatus(date);
        return false;
    }

    private void SetDayStatusCache(DateOnly date, HolidayDayStatus value)
    {
        lock (_cacheGate)
        {
            _dayStatusCache[date] = new CacheEntry<HolidayDayStatus>(
                value,
                DateTimeOffset.UtcNow.Add(_dayCacheDuration));
        }
    }

    private static JsonElement? TryGetNode(JsonElement node, params string[] path)
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

    private static string? ReadString(JsonElement? node, params string[] path)
    {
        if (!node.HasValue)
        {
            return null;
        }

        var target = path.Length == 0 ? node : TryGetNode(node.Value, path);
        if (!target.HasValue)
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

    private static int? ReadInt(JsonElement? node, params string[] path)
    {
        if (!node.HasValue)
        {
            return null;
        }

        var target = path.Length == 0 ? node : TryGetNode(node.Value, path);
        if (!target.HasValue)
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

    private static bool? ReadBool(JsonElement? node, params string[] path)
    {
        if (!node.HasValue)
        {
            return null;
        }

        var target = path.Length == 0 ? node : TryGetNode(node.Value, path);
        if (!target.HasValue)
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

    private static DateOnly? ParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return dateOnly;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        return null;
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength
            ? text
            : $"{text[..maxLength]}...";
    }

    public static string FormatDate(DateOnly date, bool isZh)
    {
        return isZh
            ? string.Create(CultureInfo.InvariantCulture, $"{date.Year}\u5e74{date.Month}\u6708{date.Day}\u65e5")
            : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

public sealed record HolidayCountdownInfo(
    string NameZh,
    string NameEn,
    DateOnly Date);
