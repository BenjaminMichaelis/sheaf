using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheaf.Data;

namespace Sheaf.Core.Tests;

[NotInParallel("sql-server")]
public class SqlServerProductionTenancyTests
{
    [ClassDataSource<ProductionSqlServerFixture>(Shared = SharedType.PerClass)]
    public required ProductionSqlServerFixture Sql { get; init; }

    [Before(Test)]
    public async Task MigrateAndResetAsync()
    {
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
        }

        await Sql.ResetAsync();
    }

    [Test]
    public async Task Read_MembershipQueryFilter_ReturnsOnlyCurrentChurch()
    {
        ProductionTwoChurchSeed seed = await SeedAsync();

        await using AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id);
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Guid[] visibleChurchIds = await db.ChurchMemberships
            .AsNoTracking()
            .Select(membership => membership.ChurchId)
            .ToArrayAsync();

        await Assert.That(visibleChurchIds).IsEquivalentTo([seed.ChurchA.Id]);
        await Assert.That(await db.ChurchMemberships.IgnoreQueryFilters().CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task SaveChangesAsync_BypassLoadedOtherChurchMembership_RejectsModification()
    {
        ProductionTwoChurchSeed seed = await SeedAsync();

        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ChurchMembership membership = await db.ChurchMemberships.IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.ChurchId == seed.ChurchB.Id);
            membership.Role = ChurchMembershipRole.Admin;

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("not the current church");
        }

        await using AsyncServiceScope verifyScope = Sql.CreateScope(seed.ChurchB.Id);
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ChurchMembership persisted = await verify.ChurchMemberships.AsNoTracking().SingleAsync();
        await Assert.That(persisted.Role).IsEqualTo(ChurchMembershipRole.Leader);
    }

    [Test]
    public async Task SaveChangesAsync_AddMembershipForAnotherChurch_RejectsWrite()
    {
        ProductionTwoChurchSeed seed = await SeedAsync();

        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChurchMemberships.Add(new ChurchMembership
            {
                ChurchId = seed.ChurchB.Id,
                UserId = seed.UserA.Id,
                Role = ChurchMembershipRole.Coordinator
            });

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("not the current church");
        }

        await using AsyncServiceScope verifyScope = Sql.CreateScope(seed.ChurchB.Id);
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await verify.ChurchMemberships.CountAsync()).IsEqualTo(1);
    }

    private async Task<ProductionTwoChurchSeed> SeedAsync()
    {
        Church churchA = new() { Slug = "production-a", Name = "Production A" };
        Church churchB = new() { Slug = "production-b", Name = "Production B" };
        ApplicationUser userA = new() { Id = "production-user-a", UserName = "production-user-a" };
        ApplicationUser userB = new() { Id = "production-user-b", UserName = "production-user-b" };

        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Churches.AddRange(churchA, churchB);
            db.Users.AddRange(userA, userB);
            await db.SaveChangesAsync();
        }

        await SeedMembershipAsync(churchA, userA);
        await SeedMembershipAsync(churchB, userB);
        return new ProductionTwoChurchSeed(churchA, churchB, userA, userB);
    }

    private async Task SeedMembershipAsync(Church church, ApplicationUser user)
    {
        await using AsyncServiceScope scope = Sql.CreateScope(church.Id);
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ChurchMemberships.Add(new ChurchMembership
        {
            ChurchId = church.Id,
            UserId = user.Id,
            Role = ChurchMembershipRole.Leader
        });
        await db.SaveChangesAsync();
    }

    private sealed record ProductionTwoChurchSeed(
        Church ChurchA,
        Church ChurchB,
        ApplicationUser UserA,
        ApplicationUser UserB);
}
