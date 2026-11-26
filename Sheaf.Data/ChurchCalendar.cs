using System.Diagnostics.CodeAnalysis;

namespace Sheaf.Data;

/// <summary>
/// Converts between a church's local calendar dates and UTC instants. Meeting dates are stored as <see cref="DateOnly"/>
/// in the church's zone; instants such as membership start and end are stored in UTC. Every rule that compares the two
/// (who was on the roster that day, the 90-day window, the edit cutoff) goes through this type.
/// </summary>
public sealed class ChurchCalendar
{
    private readonly TimeZoneInfo _zone;

    public ChurchCalendar(string timeZoneId)
    {
        if (!TryFindZone(timeZoneId, out var zone))
            throw new ArgumentException($"'{timeZoneId}' is not a known IANA time zone.", nameof(timeZoneId));
        _zone = zone;
    }

    private ChurchCalendar(TimeZoneInfo zone) => _zone = zone;

    /// <summary>Creates the calendar for <paramref name="timeZoneId"/>, or returns false if this host cannot resolve it.</summary>
    public static bool TryCreate(string? timeZoneId, [NotNullWhen(true)] out ChurchCalendar? calendar)
    {
        calendar = TryFindZone(timeZoneId, out var zone) ? new ChurchCalendar(zone) : null;
        return calendar is not null;
    }

    /// <summary>Whether <paramref name="timeZoneId"/> names a time zone this host can resolve.</summary>
    public static bool IsValidTimeZone(string? timeZoneId) => TryFindZone(timeZoneId, out _);

    /// <summary>The local calendar date at <paramref name="instant"/>.</summary>
    public DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _zone).DateTime);

    /// <summary>
    /// The UTC instants a local date spans: <c>Start</c> inclusive, <c>End</c> exclusive. A day is 23 or 25 hours when
    /// daylight saving time starts or ends on it.
    /// </summary>
    public (DateTimeOffset Start, DateTimeOffset End) DayBounds(DateOnly date) =>
        (StartOf(date), StartOf(date.AddDays(1)));

    /// <summary>The first instant of a local date.</summary>
    public DateTimeOffset StartOf(DateOnly date)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Midnight that occurs twice belongs to the day from its first occurrence, which has the larger offset.
        // Midnight that a clock change skips has no local time; the standard offset maps it to the instant of the
        // change, which is the first instant of that day.
        var offset = _zone.IsAmbiguousTime(midnight)
            ? _zone.GetAmbiguousTimeOffsets(midnight).Max()
            : _zone.GetUtcOffset(midnight);
        return new DateTimeOffset(midnight, offset).ToUniversalTime();
    }

    private static bool TryFindZone(string? timeZoneId, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        // Windows zone names also resolve on Windows hosts, but only IANA names are portable across hosts.
        // HasIanaId needs no ICU, so this check works in invariant-globalization containers too; the lookup itself
        // needs the host's tz database (the runtime image must include tzdata).
        if (string.IsNullOrWhiteSpace(timeZoneId)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found)
            || !found.HasIanaId)
            return false;
        zone = found;
        return true;
    }
}
