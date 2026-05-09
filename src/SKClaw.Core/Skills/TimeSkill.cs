using System.ComponentModel;
using System.Globalization;
using Microsoft.SemanticKernel;

namespace SKClaw.Core.Skills;

/// <summary>
/// TimeSkill — Comprehensive date/time manipulation.
/// Covers: current time, arithmetic, formatting, timezones,
/// calendar calculations, business days, durations, countdowns.
/// </summary>
public class TimeSkill
{
    // ── Basic ──────────────────────────────────────────────────

    [KernelFunction, Description("Get the current date and time in a given timezone")]
    public string GetCurrentTime(
        [Description("IANA timezone name, e.g. UTC, Asia/Jakarta, America/New_York")] string timezone = "UTC")
    {
        var tz = SafeGetTz(timezone);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        return now.ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    [KernelFunction, Description("Get current date only (no time)")]
    public string GetCurrentDate(
        [Description("IANA timezone name")] string timezone = "UTC")
    {
        var tz = SafeGetTz(timezone);
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).ToString("yyyy-MM-dd");
    }

    [KernelFunction, Description("Get the current Unix epoch timestamp (seconds)")]
    public long GetUnixTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [KernelFunction, Description("Convert a Unix timestamp to a human-readable date/time")]
    public string UnixToDateTime(
        [Description("Unix timestamp in seconds")] long timestamp,
        [Description("IANA timezone name")] string timezone = "UTC")
    {
        var tz = SafeGetTz(timezone);
        var dto = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        return TimeZoneInfo.ConvertTime(dto, tz).ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    [KernelFunction, Description("Convert a date/time string to Unix timestamp")]
    public string DateTimeToUnix([Description("Date/time string (ISO 8601)")] string dateTime)
    {
        if (!DateTimeOffset.TryParse(dateTime, out var dto))
            return "Invalid date format.";
        return dto.ToUnixTimeSeconds().ToString();
    }

    // ── Arithmetic ─────────────────────────────────────────────

    [KernelFunction, Description("Add or subtract time from a date")]
    public string AddTime(
        [Description("Base date/time (ISO 8601), or 'now'")] string date,
        [Description("Amount (can be negative)")] int amount,
        [Description("Unit: seconds, minutes, hours, days, weeks, months, years")] string unit)
    {
        var dt = date.ToLower() == "now" ? DateTimeOffset.UtcNow : ParseDateOrFail(date, out var err) ?? DateTimeOffset.UtcNow;
        var result = unit.ToLower() switch
        {
            "seconds" or "second" or "sec" => dt.AddSeconds(amount),
            "minutes" or "minute" or "min" => dt.AddMinutes(amount),
            "hours" or "hour" or "h" => dt.AddHours(amount),
            "days" or "day" or "d" => dt.AddDays(amount),
            "weeks" or "week" or "w" => dt.AddDays(amount * 7),
            "months" or "month" or "mo" => dt.AddMonths(amount),
            "years" or "year" or "y" => dt.AddYears(amount),
            _ => dt
        };
        return result.ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    [KernelFunction, Description("Calculate the difference between two dates")]
    public string DateDiff(
        [Description("Start date (ISO 8601) or 'now'")] string startDate,
        [Description("End date (ISO 8601) or 'now'")] string endDate)
    {
        var start = startDate.ToLower() == "now" ? DateTimeOffset.UtcNow : ParseDateOrFail(startDate, out _) ?? DateTimeOffset.UtcNow;
        var end   = endDate.ToLower()   == "now" ? DateTimeOffset.UtcNow : ParseDateOrFail(endDate,   out _) ?? DateTimeOffset.UtcNow;
        var diff  = end - start;
        var totalYears  = (int)Math.Abs(diff.TotalDays / 365.25);
        var totalMonths = (int)Math.Abs(diff.TotalDays / 30.44);
        return $"Total: {diff.TotalSeconds:N0} seconds | {diff.TotalMinutes:N0} minutes | {diff.TotalHours:N1} hours | {Math.Abs(diff.Days)} days | ~{totalMonths} months | ~{totalYears} years";
    }

    [KernelFunction, Description("Calculate the number of business/working days between two dates")]
    public string BusinessDaysBetween(
        [Description("Start date (ISO 8601)")] string startDate,
        [Description("End date (ISO 8601)")] string endDate)
    {
        if (!DateTimeOffset.TryParse(startDate, out var s) || !DateTimeOffset.TryParse(endDate, out var e))
            return "Invalid date format.";
        int count = 0;
        var cur = s.Date;
        while (cur <= e.Date)
        {
            if (cur.DayOfWeek != DayOfWeek.Saturday && cur.DayOfWeek != DayOfWeek.Sunday) count++;
            cur = cur.AddDays(1);
        }
        return $"{count} business days between {s:yyyy-MM-dd} and {e:yyyy-MM-dd}";
    }

    [KernelFunction, Description("Get the next occurrence of a specific weekday from a date")]
    public string NextWeekday(
        [Description("Base date (ISO 8601) or 'now'")] string fromDate,
        [Description("Weekday: Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday")] string weekday)
    {
        var from = fromDate.ToLower() == "now" ? DateTime.Today : DateTime.Parse(fromDate).Date;
        if (!Enum.TryParse<DayOfWeek>(weekday, true, out var target))
            return $"Unknown weekday: {weekday}";
        var d = from.AddDays(1);
        while (d.DayOfWeek != target) d = d.AddDays(1);
        return d.ToString("yyyy-MM-dd (dddd)");
    }

    // ── Formatting ─────────────────────────────────────────────

    [KernelFunction, Description("Format a date/time using a custom format string")]
    public string FormatDateTime(
        [Description("Date/time (ISO 8601) or 'now'")] string date,
        [Description("Format string, e.g. 'dddd, MMMM d yyyy', 'dd/MM/yyyy', 'HH:mm'")] string format,
        [Description("Culture/locale, e.g. en-US, id-ID, fr-FR")] string culture = "en-US")
    {
        var dt = date.ToLower() == "now" ? DateTimeOffset.UtcNow : ParseDateOrFail(date, out _) ?? DateTimeOffset.UtcNow;
        var ci = SafeGetCulture(culture);
        return dt.ToString(format, ci);
    }

    [KernelFunction, Description("Get human-readable relative time, e.g. '3 hours ago', 'in 2 days'")]
    public string RelativeTime([Description("Date/time (ISO 8601)")] string dateTime)
    {
        if (!DateTimeOffset.TryParse(dateTime, out var dt)) return "Invalid date.";
        var diff = DateTimeOffset.UtcNow - dt;
        var abs  = Math.Abs(diff.TotalSeconds);
        var past = diff.TotalSeconds > 0;
        string label = abs switch
        {
            < 60    => $"{(int)abs} second{((int)abs != 1 ? "s" : "")}",
            < 3600  => $"{(int)(abs/60)} minute{((int)(abs/60) != 1 ? "s" : "")}",
            < 86400 => $"{(int)(abs/3600)} hour{((int)(abs/3600) != 1 ? "s" : "")}",
            < 2592000 => $"{(int)(abs/86400)} day{((int)(abs/86400) != 1 ? "s" : "")}",
            < 31536000 => $"{(int)(abs/2592000)} month{((int)(abs/2592000) != 1 ? "s" : "")}",
            _ => $"{(int)(abs/31536000)} year{((int)(abs/31536000) != 1 ? "s" : "")}"
        };
        return past ? $"{label} ago" : $"in {label}";
    }

    // ── Calendar info ──────────────────────────────────────────

    [KernelFunction, Description("Get calendar info for a date: week number, day of year, quarter, etc.")]
    public string GetCalendarInfo([Description("Date (ISO 8601) or 'now'")] string date = "now")
    {
        var d = date.ToLower() == "now" ? DateTime.Today : DateTime.Parse(date).Date;
        var ci = CultureInfo.InvariantCulture;
        var week = ci.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"""
            Date: {d:yyyy-MM-dd}
            Day of week: {d:dddd} (#{(int)d.DayOfWeek})
            Day of year: {d.DayOfYear}
            Week of year: {week}
            Month: {d:MMMM} ({d.Month})
            Quarter: Q{(d.Month - 1) / 3 + 1}
            Year: {d.Year} (leap year: {DateTime.IsLeapYear(d.Year)})
            Days in month: {DateTime.DaysInMonth(d.Year, d.Month)}
            """;
    }

    [KernelFunction, Description("Get the start and end dates of a week, month, or quarter for a given date")]
    public string GetPeriodBounds(
        [Description("Date (ISO 8601) or 'now'")] string date,
        [Description("Period: week, month, quarter, year")] string period)
    {
        var d = date.ToLower() == "now" ? DateTime.Today : DateTime.Parse(date).Date;
        DateTime start, end;
        switch (period.ToLower())
        {
            case "week":
                int dow = (int)d.DayOfWeek;
                int diff = (dow == 0) ? -6 : 1 - dow;
                start = d.AddDays(diff);
                end = start.AddDays(6);
                break;
            case "month":
                start = new DateTime(d.Year, d.Month, 1);
                end = start.AddMonths(1).AddDays(-1);
                break;
            case "quarter":
                int q = (d.Month - 1) / 3;
                start = new DateTime(d.Year, q * 3 + 1, 1);
                end = start.AddMonths(3).AddDays(-1);
                break;
            case "year":
                start = new DateTime(d.Year, 1, 1);
                end = new DateTime(d.Year, 12, 31);
                break;
            default:
                return $"Unknown period: {period}";
        }
        return $"{period} containing {d:yyyy-MM-dd}: Start={start:yyyy-MM-dd}, End={end:yyyy-MM-dd}";
    }

    [KernelFunction, Description("Count down to a future date/event")]
    public string Countdown(
        [Description("Target date/time (ISO 8601)")] string targetDate,
        [Description("Event name (optional)")] string eventName = "")
    {
        if (!DateTimeOffset.TryParse(targetDate, out var target)) return "Invalid date.";
        var diff = target - DateTimeOffset.UtcNow;
        if (diff.TotalSeconds <= 0) return $"{(string.IsNullOrEmpty(eventName) ? "Event" : eventName)} has already passed.";
        var label = string.IsNullOrEmpty(eventName) ? "" : $"Until {eventName}: ";
        return $"{label}{(int)diff.TotalDays}d {diff.Hours}h {diff.Minutes}m {diff.Seconds}s remaining";
    }

    [KernelFunction, Description("List all timezones matching a search term")]
    public string ListTimezones([Description("Search term, e.g. 'Asia', 'Pacific', 'UTC'")] string search = "")
    {
        var zones = TimeZoneInfo.GetSystemTimeZones()
            .Where(z => string.IsNullOrEmpty(search) ||
                        z.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        z.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .Select(z => $"{z.Id} (UTC{z.BaseUtcOffset:hh\\:mm})");
        return string.Join("\n", zones);
    }

    [KernelFunction, Description("Convert a time from one timezone to another")]
    public string ConvertTimezone(
        [Description("Date/time (ISO 8601)")] string dateTime,
        [Description("Source timezone (IANA or 'local')")] string fromZone,
        [Description("Target timezone (IANA)")] string toZone)
    {
        if (!DateTimeOffset.TryParse(dateTime, out var dt)) return "Invalid date.";
        var from = SafeGetTz(fromZone);
        var to   = SafeGetTz(toZone);
        // Interpret the parsed DateTimeOffset as a local time in the source timezone
        var converted = TimeZoneInfo.ConvertTime(dt.DateTime, from, to);
        return $"{converted:yyyy-MM-dd HH:mm:ss} {to.Id}";
    }

    // ── Helpers ────────────────────────────────────────────────
    private static TimeZoneInfo SafeGetTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { return TimeZoneInfo.Utc; }
    }
    private static CultureInfo SafeGetCulture(string name)
    {
        try { return new CultureInfo(name); } catch { return CultureInfo.InvariantCulture; }
    }
    private static DateTimeOffset? ParseDateOrFail(string s, out string err)
    {
        err = "";
        if (DateTimeOffset.TryParse(s, out var d)) return d;
        err = $"Cannot parse '{s}'";
        return null;
    }
}
