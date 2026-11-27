using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

namespace Sheaf.Core.Tests;

[NotInParallel("sql-server")]
public class SqlServerAttendanceTests
{
    [ClassDataSource<ProductionSqlServerFixture>(Shared = SharedType.PerClass)]
    public required ProductionSqlServerFixture Sql { get; init; }

    [Before(Test)]
    public async Task MigrateAndResetAsync()
    {
        await Sql.InDatabaseScopeAsync(null, db => db.Database.MigrateAsync());
        await Sql.ResetAsync();
    }

    [Test]
    public async Task SaveChangesAsync_SamePersonTwiceInSession_RejectsDuplicate()
    {
        var (churchId, session, person) = await SeedSessionAsync();

        await Sql.InDatabaseScopeAsync(churchId, async db =>
        {
            db.Attendances.Add(new Attendance { SessionId = session.Id, PersonId = person.Id });

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        });
    }

    [Test]
    public async Task SaveChangesAsync_PersonReaddedAfterRemoval_KeepsRemovedRowAsHistory()
    {
        var (churchId, session, person) = await SeedSessionAsync();

        await Sql.InDatabaseScopeAsync(churchId, async db =>
        {
            var present = await db.Attendances.SingleAsync();
            present.RemovedAt = present.RecordedAt.AddMinutes(1);
            present.RemovedByUserId = "leader";
            await db.SaveChangesAsync();

            db.Attendances.Add(new Attendance { SessionId = session.Id, PersonId = person.Id });
            await db.SaveChangesAsync();
        });

        var rows = await Sql.InDatabaseScopeAsync(churchId, db => db.Attendances.AsNoTracking()
            .Select(attendance => attendance.RemovedAt == null).ToArrayAsync());
        await Assert.That(rows).IsEquivalentTo([true, false]);
    }

    [Test]
    public async Task SaveChangesAsync_RemovedByWithoutRemovedAt_RejectsRow()
    {
        var (churchId, _, _) = await SeedSessionAsync();

        await Sql.InDatabaseScopeAsync(churchId, async db =>
        {
            (await db.Attendances.SingleAsync()).RemovedByUserId = "leader";

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        });
    }

    [Test]
    public async Task SaveChangesAsync_SecondSessionSameDate_AllowsBoth()
    {
        var (churchId, session, _) = await SeedSessionAsync();

        await Sql.InDatabaseScopeAsync(churchId, async db =>
        {
            db.ContextSessions.Add(new ContextSession { ContextId = session.ContextId, OccurredOn = session.OccurredOn });
            await db.SaveChangesAsync();
        });

        var count = await Sql.InDatabaseScopeAsync(churchId, db => db.ContextSessions.CountAsync());
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task SaveChangesAsync_ChurchWithBlankTimeZone_RejectsInsert()
    {
        await Sql.InDatabaseScopeAsync(null, async db =>
        {
            db.Churches.Add(new Church { Slug = "blank-zone", Name = "Blank zone", TimeZoneId = " " });

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        });
    }

    /// <summary>Seeds church A with one group meeting on a Sunday and one person marked present.</summary>
    private async Task<(Guid ChurchId, ContextSession Session, Person Person)> SeedSessionAsync()
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        var churchId = seed.ChurchA.Id;
        var group = new Sheaf.Data.Context { Name = "Sunday group", StartsAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var person = new Person { FirstName = "Ada" };
        var session = new ContextSession { ContextId = group.Id, OccurredOn = new DateOnly(2026, 11, 22), RecordedByUserId = seed.UserA.Id };

        await Sql.InDatabaseScopeAsync(churchId, async db =>
        {
            db.Contexts.Add(group);
            db.People.Add(person);
            db.ContextSessions.Add(session);
            db.Attendances.Add(new Attendance { SessionId = session.Id, PersonId = person.Id, RecordedByUserId = seed.UserA.Id });
            await db.SaveChangesAsync();
        });
        return (churchId, session, person);
    }
}
