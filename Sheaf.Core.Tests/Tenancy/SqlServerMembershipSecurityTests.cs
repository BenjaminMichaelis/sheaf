using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

namespace Sheaf.Core.Tests;

[NotInParallel("sql-server")]
public class SqlServerMembershipSecurityTests
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteDelete_ProductionRegistration_RejectsMemberships(bool useAsync)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);

        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            await Assert.That(async () => await DeleteAsync(db.ChurchMemberships, useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ExecuteDelete cannot remove church memberships");
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_ProductionRegistration_RejectsRoleAndUserIdAssignments(bool useAsync)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);

        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            await Assert.That(async () => await UpdateMembershipRoleAsync(db, useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ChurchMembership.Role");
            await Assert.That(async () => await UpdateUserIdAsync(db, useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ChurchMembership.UserId");
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_ProductionRegistration_RejectsProjectedRoleAssignment(bool useAsync)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);

        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            var query = db.ChurchMemberships.Select(membership => new { membership.Role });
            await Assert.That(async () => useAsync
                    ? await query.ExecuteUpdateAsync(
                        setters => setters.SetProperty(projection => projection.Role, ChurchMembershipRole.Admin))
                    : query.ExecuteUpdate(
                        setters => setters.SetProperty(projection => projection.Role, ChurchMembershipRole.Admin)))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ChurchMembership.Role");
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    private static Task<int> DeleteAsync(IQueryable<ChurchMembership> memberships, bool useAsync) =>
        useAsync
            ? memberships.ExecuteDeleteAsync()
            : Task.FromResult(memberships.ExecuteDelete());

    private static Task<int> UpdateMembershipRoleAsync(ApplicationDbContext db, bool useAsync) =>
        useAsync
            ? db.ChurchMemberships.ExecuteUpdateAsync(
                setters => setters.SetProperty(membership => membership.Role, ChurchMembershipRole.Admin))
            : Task.FromResult(db.ChurchMemberships.ExecuteUpdate(
                setters => setters.SetProperty(membership => membership.Role, ChurchMembershipRole.Admin)));

    private static Task<int> UpdateUserIdAsync(ApplicationDbContext db, bool useAsync) =>
        useAsync
            ? db.ChurchMemberships.ExecuteUpdateAsync(
                setters => setters.SetProperty(membership => membership.UserId, "other"))
            : Task.FromResult(db.ChurchMemberships.ExecuteUpdate(
                setters => setters.SetProperty(membership => membership.UserId, "other")));
}
