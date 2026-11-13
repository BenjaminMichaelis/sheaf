using Sheaf.Data;

namespace Sheaf.Core.Tests;

internal static class TenancyTestData
{
    public static async Task<TwoChurchScenario> SeedTwoChurchesAsync(ProductionSqlServerFixture sql)
    {
        Church churchA = new() { Slug = "production-a", Name = "Production A" };
        Church churchB = new() { Slug = "production-b", Name = "Production B" };
        ApplicationUser userA = new() { Id = "production-user-a", UserName = "production-user-a" };
        ApplicationUser userB = new() { Id = "production-user-b", UserName = "production-user-b" };
        ChurchMembership membershipA = new() { ChurchId = churchA.Id, UserId = userA.Id, Role = ChurchMembershipRole.Leader };
        ChurchMembership membershipB = new() { ChurchId = churchB.Id, UserId = userB.Id, Role = ChurchMembershipRole.Leader };

        await sql.InDatabaseScopeAsync(null, async db =>
        {
            db.Churches.AddRange(churchA, churchB);
            db.Users.AddRange(userA, userB);
            await db.SaveChangesAsync();
        });

        foreach (ChurchMembership membership in new[] { membershipA, membershipB })
        {
            await sql.InDatabaseScopeAsync(membership.ChurchId, async db =>
            {
                db.ChurchMemberships.Add(membership);
                await db.SaveChangesAsync();
            });
        }

        return new(churchA, churchB, userA, userB, membershipA, membershipB);
    }
}
