using System;
using System.Globalization;

namespace LanMontainDesktop.Services;

public sealed class LunarCalendarService
{
    private static readonly ChineseLunisolarCalendar Calendar = new();

    private static readonly string[] HeavenlyStemsZh =
    [
        "\u7532",
        "\u4e59",
        "\u4e19",
        "\u4e01",
        "\u620a",
        "\u5df1",
        "\u5e9a",
        "\u8f9b",
        "\u58ec",
        "\u7678"
    ];

    private static readonly string[] EarthlyBranchesZh =
    [
        "\u5b50",
        "\u4e11",
        "\u5bc5",
        "\u536f",
        "\u8fb0",
        "\u5df3",
        "\u5348",
        "\u672a",
        "\u7533",
        "\u9149",
        "\u620c",
        "\u4ea5"
    ];

    private static readonly string[] HeavenlyStemsEn =
        ["Jia", "Yi", "Bing", "Ding", "Wu", "Ji", "Geng", "Xin", "Ren", "Gui"];

    private static readonly string[] EarthlyBranchesEn =
        ["Zi", "Chou", "Yin", "Mao", "Chen", "Si", "Wu", "Wei", "Shen", "You", "Xu", "Hai"];

    private static readonly string[] ZodiacsZh =
    [
        "\u9f20",
        "\u725b",
        "\u864e",
        "\u5154",
        "\u9f99",
        "\u86c7",
        "\u9a6c",
        "\u7f8a",
        "\u7334",
        "\u9e21",
        "\u72d7",
        "\u732a"
    ];

    private static readonly string[] ZodiacsEn =
        ["Rat", "Ox", "Tiger", "Rabbit", "Dragon", "Snake", "Horse", "Goat", "Monkey", "Rooster", "Dog", "Pig"];

    private static readonly string[] LunarMonthsZh =
    [
        "\u6b63",
        "\u4e8c",
        "\u4e09",
        "\u56db",
        "\u4e94",
        "\u516d",
        "\u4e03",
        "\u516b",
        "\u4e5d",
        "\u5341",
        "\u51ac",
        "\u814a"
    ];

    private static readonly string[] LunarDayDigitsZh =
    [
        "\u4e00",
        "\u4e8c",
        "\u4e09",
        "\u56db",
        "\u4e94",
        "\u516d",
        "\u4e03",
        "\u516b",
        "\u4e5d",
        "\u5341"
    ];

    public LunarCalendarInfo GetLunarInfo(DateTime dateTime)
    {
        var date = dateTime.Date;
        try
        {
            var lunarYear = Calendar.GetYear(date);
            var rawLunarMonth = Calendar.GetMonth(date);
            var lunarDay = Calendar.GetDayOfMonth(date);

            var (lunarMonth, isLeapMonth) = NormalizeLunarMonth(lunarYear, rawLunarMonth);

            var sexagenaryYear = Calendar.GetSexagenaryYear(date);
            var stemIndex = Calendar.GetCelestialStem(sexagenaryYear) - 1;
            var branchIndex = Calendar.GetTerrestrialBranch(sexagenaryYear) - 1;

            var ganzhiYearZh = $"{HeavenlyStemsZh[stemIndex]}{EarthlyBranchesZh[branchIndex]}";
            var ganzhiYearEn = $"{HeavenlyStemsEn[stemIndex]}-{EarthlyBranchesEn[branchIndex]}";
            var zodiacZh = ZodiacsZh[branchIndex];
            var zodiacEn = ZodiacsEn[branchIndex];

            return new LunarCalendarInfo(
                LunarYear: lunarYear,
                LunarMonth: lunarMonth,
                LunarDay: lunarDay,
                IsLeapMonth: isLeapMonth,
                LunarDateZh: BuildLunarDateZh(lunarMonth, lunarDay, isLeapMonth),
                LunarDateEn: BuildLunarDateEn(lunarMonth, lunarDay, isLeapMonth),
                GanzhiYearZh: ganzhiYearZh,
                GanzhiYearEn: ganzhiYearEn,
                ZodiacZh: zodiacZh,
                ZodiacEn: zodiacEn);
        }
        catch (ArgumentOutOfRangeException)
        {
            // ChineseLunisolarCalendar has a limited date range.
            return new LunarCalendarInfo(
                LunarYear: date.Year,
                LunarMonth: date.Month,
                LunarDay: date.Day,
                IsLeapMonth: false,
                LunarDateZh: "\u65e5\u671f\u8d85\u51fa\u519c\u5386\u652f\u6301\u8303\u56f4",
                LunarDateEn: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                GanzhiYearZh: "-",
                GanzhiYearEn: "-",
                ZodiacZh: "-",
                ZodiacEn: "-");
        }
    }

    private static (int Month, bool IsLeapMonth) NormalizeLunarMonth(int lunarYear, int rawMonth)
    {
        var leapMonth = Calendar.GetLeapMonth(lunarYear);
        if (leapMonth == 0)
        {
            return (rawMonth, false);
        }

        if (rawMonth == leapMonth)
        {
            return (rawMonth - 1, true);
        }

        if (rawMonth > leapMonth)
        {
            return (rawMonth - 1, false);
        }

        return (rawMonth, false);
    }

    private static string BuildLunarDateZh(int lunarMonth, int lunarDay, bool isLeapMonth)
    {
        var monthName = lunarMonth is >= 1 and <= 12
            ? LunarMonthsZh[lunarMonth - 1]
            : lunarMonth.ToString(CultureInfo.InvariantCulture);
        var leapPrefix = isLeapMonth ? "\u95f0" : string.Empty;
        return $"{leapPrefix}{monthName}\u6708{BuildLunarDayZh(lunarDay)}";
    }

    private static string BuildLunarDateEn(int lunarMonth, int lunarDay, bool isLeapMonth)
    {
        var leapPrefix = isLeapMonth ? "Leap " : string.Empty;
        return $"{leapPrefix}M{lunarMonth} D{lunarDay}";
    }

    private static string BuildLunarDayZh(int day)
    {
        if (day <= 0)
        {
            return day.ToString(CultureInfo.InvariantCulture);
        }

        if (day <= 10)
        {
            return day == 10 ? "\u521d\u5341" : $"\u521d{LunarDayDigitsZh[day - 1]}";
        }

        if (day < 20)
        {
            return $"\u5341{LunarDayDigitsZh[day - 11]}";
        }

        if (day == 20)
        {
            return "\u4e8c\u5341";
        }

        if (day < 30)
        {
            return $"\u5eff{LunarDayDigitsZh[day - 21]}";
        }

        if (day == 30)
        {
            return "\u4e09\u5341";
        }

        return day.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed record LunarCalendarInfo(
    int LunarYear,
    int LunarMonth,
    int LunarDay,
    bool IsLeapMonth,
    string LunarDateZh,
    string LunarDateEn,
    string GanzhiYearZh,
    string GanzhiYearEn,
    string ZodiacZh,
    string ZodiacEn);

