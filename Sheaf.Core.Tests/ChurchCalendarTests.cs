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
    public async Task StartOf_MidnightSkippedBeforeAsuncionChangedStandardOffset_UsesOffsetInEffectThen()
    {
        // Paraguay moved clocks from 00:00 -04 to 01:00 -03 on 1 October 2023, then kept -03 as its standard offset
        // from October 2024. The day still starts at 00:00 -04 then, not at today's standard offset.
        var calendar = new ChurchCalendar("America/Asuncion");
        var date = new DateOnly(2023, 10, 1);

        var start = calendar.StartOf(date);

        await Assert.That(start).IsEqualTo(new DateTimeOffset(2023, 10, 1, 4, 0, 0, TimeSpan.Zero));
        await Assert.That(calendar.DateOf(start.AddMinutes(-1))).IsEqualTo(date.AddDays(-1));
    }

    [Test]
    [Arguments("America/Asuncion")]
    [Arguments("America/Santiago")]
    [Arguments("America/Havana")]
    [Arguments("Asia/Tehran")]
    [Arguments("America/Caracas")]
    [Arguments("Europe/Moscow")]
    [Arguments("America/Sao_Paulo")]
    public async Task StartOf_EveryDateFrom1995To2030_IsFirstInstantOfThatLocalDate(string zone)
    {
        // These zones skipped or repeated midnight, or changed their standard offset, during this range. Each day must
        // start at the first instant the zone itself places on that date. A minute before stands in for "the instant
        // before", because Windows places some changes at 23:59:59.999.
        var calendar = new ChurchCalendar(zone);
        var wrong = new List<string>();
        for (var date = new DateOnly(1995, 1, 1); date < new DateOnly(2031, 1, 1); date = date.AddDays(1))
        {
            var start = calendar.StartOf(date);
            if (calendar.DateOf(start) != date || calendar.DateOf(start.AddMinutes(-1)) != date.AddDays(-1))
                wrong.Add($"{date:yyyy-MM-dd} starts at {start:u}");
        }

        await Assert.That(wrong).IsEmpty();
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
