using System;
using System.Collections.ObjectModel;

namespace LanMountainDesktop.Services;

/// <summary>
/// 时区服务，提供时区信息和时间转换功能
/// </summary>
public sealed class TimeZoneService
{
    private TimeZoneInfo _currentTimeZone = TimeZoneInfo.Local;

    /// <summary>
    /// 当前选中的时区
    /// </summary>
    public TimeZoneInfo CurrentTimeZone
    {
        get => _currentTimeZone;
        set
        {
            if (_currentTimeZone != value)
            {
                _currentTimeZone = value;
                TimeZoneChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 时区变更事件
    /// </summary>
    public event EventHandler? TimeZoneChanged;

    /// <summary>
    /// 获取所有可用的时区
    /// </summary>
    public ReadOnlyCollection<TimeZoneInfo> GetAllTimeZones()
    {
        return TimeZoneInfo.GetSystemTimeZones();
    }

    /// <summary>
    /// 获取当前时区的当前时间
    /// </summary>
    public DateTime GetCurrentTime()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _currentTimeZone);
    }

    /// <summary>
    /// 根据时区ID设置当前时区
    /// </summary>
    public bool SetTimeZoneById(string timeZoneId)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            CurrentTimeZone = timeZone;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 获取时区显示名称（包含UTC偏移）
    /// </summary>
    public string GetTimeZoneDisplayName(TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var hours = Math.Abs(offset.Hours);
        var minutes = Math.Abs(offset.Minutes);
        
        return $"(UTC{sign}{hours:D2}:{minutes:D2}) {timeZone.DisplayName}";
    }

    /// <summary>
    /// 获取常用时区列表
    /// </summary>
    public TimeZoneInfo[] GetCommonTimeZones()
    {
        return new[]
        {
            TimeZoneInfo.Local, // 本地时区
            TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"), // 北京时间
            TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"), // 东京时间
            TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"), // 太平洋时间
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"), // 东部时间
            TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"), // 中欧时间
            TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"), // 伦敦时间
            TimeZoneInfo.FindSystemTimeZoneById("UTC"), // 协调世界时
        };
    }
}
