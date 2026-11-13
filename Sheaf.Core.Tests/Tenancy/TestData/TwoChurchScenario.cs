using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

namespace Sheaf.Core.Tests;

internal sealed record TwoChurchScenario(
    Church ChurchA,
    Church ChurchB,
    ApplicationUser UserA,
    ApplicationUser UserB,
    ChurchMembership MembershipA,
    ChurchMembership MembershipB)
{
    public async Task AssertMembershipsAsync(
        ProductionSqlServerFixture sql,
        ChurchMembershipRole roleA = ChurchMembershipRole.Leader,
        ChurchMembershipRole roleB = ChurchMembershipRole.Leader)
    {
        ChurchMembership[] persisted = await sql.InDatabaseScopeAsync(null,
            db => db.ChurchMemberships.IgnoreQueryFilters().AsNoTracking().ToArrayAsync());

        await Assert.That(persisted.Select(membership => (membership.Id, membership.ChurchId, membership.UserId, membership.Role)))
            .IsEquivalentTo([
                (MembershipA.Id, ChurchA.Id, UserA.Id, roleA),
                (MembershipB.Id, ChurchB.Id, UserB.Id, roleB)
            ]);
    }
}
