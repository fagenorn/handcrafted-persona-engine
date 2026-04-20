namespace PersonaEngine.Lib.UI.ControlPanel;

using System.Collections.Frozen;

/// <summary>
///     Shared humanised time formatters used by control-panel widgets that display
///     "Last tested N ago" / "Updated M ago" footers. The hot path (per-frame text
///     for the footer) allocates zero strings: bucketed values (0–59s, 0–59m,
///     0–23h, 0–6d) resolve via a frozen lookup, and anything rarer reuses a
///     thread-local buffer through <see cref="string.Create{TState}" />.
/// </summary>
public static class TimeFormat
{
    private const int SecondsPerMinute = 60;

    private const int MinutesPerHour = 60;

    private const int HoursPerDay = 24;

    private const int DaysInCache = 7;

    /// <summary>"just now" — value for <see cref="HumaniseAgo" /> when &lt; 1 second.</summary>
    public const string JustNow = "just now";

    private static readonly FrozenDictionary<int, string> SecondsCache = BuildSecondsCache();

    private static readonly FrozenDictionary<int, string> MinutesCache = BuildMinutesCache();

    private static readonly FrozenDictionary<int, string> HoursCache = BuildHoursCache();

    private static readonly FrozenDictionary<int, string> DaysCache = BuildDaysCache();

    /// <summary>
    ///     Formats a positive <see cref="TimeSpan" /> as a short "ago" string
    ///     ("just now", "5s ago", "12m ago", "3h ago", "4d ago"). Negative or zero
    ///     spans return <see cref="JustNow" />. The common path is fully cached;
    ///     values past 6 days fall through to a single rare allocation.
    /// </summary>
    public static string HumaniseAgo(TimeSpan ago)
    {
        if (ago.Ticks < TimeSpan.TicksPerSecond)
        {
            return JustNow;
        }

        if (ago.Ticks < TimeSpan.TicksPerMinute)
        {
            return SecondsCache[(int)(ago.Ticks / TimeSpan.TicksPerSecond)];
        }

        if (ago.Ticks < TimeSpan.TicksPerHour)
        {
            return MinutesCache[(int)(ago.Ticks / TimeSpan.TicksPerMinute)];
        }

        if (ago.Ticks < TimeSpan.TicksPerDay)
        {
            return HoursCache[(int)(ago.Ticks / TimeSpan.TicksPerHour)];
        }

        var days = (int)(ago.Ticks / TimeSpan.TicksPerDay);
        return days < DaysInCache ? DaysCache[days] : FormatDays(days);
    }

    private static string FormatDays(int days) =>
        string.Create(null, stackalloc char[16], $"{days}d ago");

    private static FrozenDictionary<int, string> BuildSecondsCache()
    {
        var map = new Dictionary<int, string>(SecondsPerMinute);
        for (var i = 1; i < SecondsPerMinute; i++)
        {
            map[i] = $"{i}s ago";
        }

        return map.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, string> BuildMinutesCache()
    {
        var map = new Dictionary<int, string>(MinutesPerHour);
        for (var i = 1; i < MinutesPerHour; i++)
        {
            map[i] = $"{i}m ago";
        }

        return map.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, string> BuildHoursCache()
    {
        var map = new Dictionary<int, string>(HoursPerDay);
        for (var i = 1; i < HoursPerDay; i++)
        {
            map[i] = $"{i}h ago";
        }

        return map.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, string> BuildDaysCache()
    {
        var map = new Dictionary<int, string>(DaysInCache);
        for (var i = 1; i < DaysInCache; i++)
        {
            map[i] = $"{i}d ago";
        }

        return map.ToFrozenDictionary();
    }
}
