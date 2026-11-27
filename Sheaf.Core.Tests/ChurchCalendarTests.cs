using Sheaf.Data;

namespace Sheaf.Core.Tests;

public class ChurchCalendarTests
{
    private const string Chicago = "America/Chicago";

    [Test]
    public async Task StartOf_WinterDateInChicago_ReturnsLocalMidnightInUtc()
    {
        var calendar = new ChurchCalendar(Chicago);

        await Assert.That(calendar.StartOf(new DateOnly(2026, 11, 22)))
            .IsEqualTo(new DateTimeOffset(2026, 11, 22, 6, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task DateOf_SundayEveningInChicago_ReturnsSundayAlthoughUtcIsMonday()
    {
        var calendar = new ChurchCalendar(Chicago);

        await Assert.That(calendar.DateOf(new DateTimeOffset(2026, 11, 23, 3, 0, 0, TimeSpan.Zero)))
            .IsEqualTo(new DateOnly(2026, 11, 22));
    }

    [Test]
    [Arguments(2026, 3, 8, 23)]
    [Arguments(2026, 11, 1, 25)]
    [Arguments(2026, 6, 14, 24)]
    public async Task DayBounds_DaylightSavingChangeInChicago_SpansLocalDayLength(int year, int month, int day, int hours)
    {
        var calendar = new ChurchCalendar(Chicago);

        var (start, end) = calendar.DayBounds(new DateOnly(year, month, day));

        await Assert.That(end - start).IsEqualTo(TimeSpan.FromHours(hours));
        await Assert.That(calendar.DateOf(start)).IsEqualTo(new DateOnly(year, month, day));
        await Assert.That(calendar.DateOf(end.AddTicks(-1))).IsEqualTo(new DateOnly(year, month, day));
    }

    [Test]
    public async Task StartOf_MidnightSkippedBySantiagoClockChange_ReturnsInstantOfChange()
    {
        // Chile moves its clocks from 00:00 to 01:00 on the first Sunday of September, so that day has no midnight.
        var calendar = new ChurchCalendar("America/Santiago");
        var date = new DateOnly(2026, 9, 6);

        var (start, end) = calendar.DayBounds(date);

        await Assert.That(start).IsEqualTo(new DateTimeOffset(2026, 9, 6, 4, 0, 0, TimeSpan.Zero));
        await Assert.That(end - start).IsEqualTo(TimeSpan.FromHours(23));
        await Assert.That(calendar.DateOf(start)).IsEqualTo(date);
        // Windows time zone data places this change at 23:59:59.999, so check a minute before rather than a tick.
        await Assert.That(calendar.DateOf(start.AddMinutes(-1))).IsEqualTo(date.AddDays(-1));
    }

    [Test]
    [Arguments(Chicago, true)]
    [Arguments("Etc/UTC", true)]
    [Arguments("Central Standard Time", false)]
    [Arguments("Mars/Olympus_Mons", false)]
    [Arguments("", false)]
    [Arguments(null, false)]
    public async Task IsValidTimeZone_ZoneName_AcceptsOnlyKnownIanaNames(string? timeZoneId, bool expected)
    {
        await Assert.That(ChurchCalendar.IsValidTimeZone(timeZoneId)).IsEqualTo(expected);
    }

    [Test]
    public async Task Constructor_UnknownZone_ThrowsArgumentException()
    {
        await Assert.That(() => new ChurchCalendar("Mars/Olympus_Mons")).Throws<ArgumentException>();
    }
}
