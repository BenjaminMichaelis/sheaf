using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Data;

using DomainCapture = Sheaf.Data.Capture;

namespace Sheaf.Core.Tests;

public class SqlServerBulkUpdateCompatibilityTests
{
    [ClassDataSource<ProductionSqlServerFixture>]
    public required ProductionSqlServerFixture Sql { get; init; }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_MigratedOfTypeStatus_OnlyUpdatesCurrentChurchRows(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string updatedStatus = "updated-through-of-type";
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            int affected = await UpdateAsync(db.Captures.OfType<DomainCapture>(),
                setters => setters.SetProperty(row => row.Status, updatedStatus), useAsync);

            await Assert.That(affected).IsEqualTo(1);
        }

        await AssertCapturesAsync(seed, updatedStatus);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_MigratedOfTypeWithoutGuard_ProviderSupportsPayloadAndOwnership(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext configured = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using ApplicationDbContext db = CreateProviderControl(configured, seed.ChurchA.Church.Id);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                IQueryable<DomainCapture> query = db.Captures.OfType<DomainCapture>();

                await Assert.That(await UpdateAsync(query,
                    setters => setters.SetProperty(row => row.Status, "provider-control"), useAsync)).IsEqualTo(1);
                await Assert.That(await UpdateAsync(query,
                    setters => setters.SetProperty(row => row.ChurchId, seed.ChurchB.Church.Id), useAsync)).IsEqualTo(1);

                DomainCapture moved = await db.Captures.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(row => row.Id == seed.ChurchA.Capture.Id);
                await Assert.That(moved.ChurchId).IsEqualTo(seed.ChurchB.Church.Id);
                await Assert.That(moved.Status).IsEqualTo("provider-control");
                await transaction.RollbackAsync();
            });
        }

        await AssertCapturesAsync(seed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_MigratedCastStatus_OnlyUpdatesCurrentChurchRows(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string updatedStatus = "updated-through-cast";
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            int affected = await UpdateAsync(TypeOperatorCaptures(db, useCast: true),
                setters => setters.SetProperty(row => row.Status, updatedStatus), useAsync);

            await Assert.That(affected).IsEqualTo(1);
        }

        await AssertCapturesAsync(seed, updatedStatus);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_MigratedCastWithoutGuard_ProviderSupportsPayloadAndOwnership(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext configured = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using ApplicationDbContext db = CreateProviderControl(configured, seed.ChurchA.Church.Id);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                IQueryable<DomainCapture> query = TypeOperatorCaptures(db, useCast: true);
                var projection = query.Select(row => new { Owner = row.ChurchId });

                await Assert.That(await UpdateAsync(query,
                    setters => setters.SetProperty(row => row.Status, "provider-control"), useAsync)).IsEqualTo(1);
                await Assert.That(await UpdateAsync(projection,
                    setters => setters.SetProperty(row => row.Owner, seed.ChurchB.Church.Id), useAsync)).IsEqualTo(1);

                DomainCapture moved = await db.Captures.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(row => row.Id == seed.ChurchA.Capture.Id);
                await Assert.That(moved.ChurchId).IsEqualTo(seed.ChurchB.Church.Id);
                await Assert.That(moved.Status).IsEqualTo("provider-control");
                await transaction.RollbackAsync();
            });
        }

        await AssertCapturesAsync(seed);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdate_MigratedTypeOperatorPayloadAlias_OnlyUpdatesCurrentChurchRows(bool useCast, bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string updatedStatus = """{"ChurchId":"ordinary type-operator payload"}""";
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var query = TypeOperatorCaptures(db, useCast).Select(row => new { ChurchId = row.Status });

            int affected = await UpdateAsync(query,
                setters => setters.SetProperty(row => row.ChurchId, updatedStatus), useAsync);

            await Assert.That(affected).IsEqualTo(1);
        }

        await AssertCapturesAsync(seed, updatedStatus);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdate_MigratedTypeOperatorOwnership_ThrowsBeforeMutation(bool useCast, bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            IQueryable<DomainCapture> query = TypeOperatorCaptures(db, useCast);

            await AssertImmutableAsync(() => UpdateAsync(query,
                setters => setters.SetProperty(row => row.ChurchId, seed.ChurchB.Church.Id), useAsync));

            var projection = query.Select(row => new { Owner = row.ChurchId, ChurchId = row.Status });
            await AssertImmutableAsync(() => UpdateAsync(projection,
                setters => setters.SetProperty(row => row.Owner, seed.ChurchB.Church.Id), useAsync));
        }

        await AssertCapturesAsync(seed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_MigratedUnionStatus_OnlyUpdatesCurrentChurchRows(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string updatedStatus = "updated-through-union";
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            IQueryable<DomainCapture> query = UnionCaptures(db);

            int affected = await UpdateAsync(query,
                setters => setters.SetProperty(row => row.Status, updatedStatus), useAsync);

            await Assert.That(affected).IsEqualTo(1);
        }

        await AssertCapturesAsync(seed, updatedStatus);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_MigratedCorrelatedSelectManyStatus_OnlyUpdatesCurrentChurchRows(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string updatedStatus = "updated-through-correlated-select-many";
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            IQueryable<DomainCapture> query = CorrelatedCaptures(db);

            int affected = await UpdateAsync(query,
                setters => setters.SetProperty(row => row.Status, updatedStatus), useAsync);

            await Assert.That(affected).IsEqualTo(1);
        }

        await AssertCapturesAsync(seed, updatedStatus);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdateAsync_MigratedSourcesWithoutGuard_ProviderSupportsPayloadAndOwnership(bool useUnion)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext configured = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using ApplicationDbContext db = CreateProviderControl(configured, seed.ChurchA.Church.Id);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                IQueryable<DomainCapture> query = useUnion ? UnionCaptures(db) : CorrelatedCaptures(db);

                await Assert.That(await query.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Status, "provider-control"))).IsEqualTo(1);
                await Assert.That(await query.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.ChurchId, seed.ChurchB.Church.Id))).IsEqualTo(1);

                DomainCapture moved = await db.Captures.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(row => row.Id == seed.ChurchA.Capture.Id);
                await Assert.That(moved.ChurchId).IsEqualTo(seed.ChurchB.Church.Id);
                await Assert.That(moved.Status).IsEqualTo("provider-control");
                await transaction.RollbackAsync();
            });
        }

        await AssertCapturesAsync(seed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdateAsync_MigratedCorrelatedAliasWithoutGuard_ProviderSupportsOwnership(bool useInner)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext configured = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using ApplicationDbContext db = CreateProviderControl(configured, seed.ChurchA.Church.Id);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var query = useInner
                    ? db.Captures.SelectMany(outer => db.Captures.Where(inner => inner.Id == outer.Id),
                        (outer, inner) => new { Owner = inner.ChurchId, ChurchId = inner.Status })
                    : db.Captures.SelectMany(outer => db.Captures.Where(inner => inner.Id == outer.Id),
                        (outer, inner) => new { Owner = outer.ChurchId, ChurchId = outer.Status });

                await Assert.That(await query.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.ChurchId, "provider-control"))).IsEqualTo(1);
                await Assert.That(await query.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Owner, seed.ChurchB.Church.Id))).IsEqualTo(1);

                DomainCapture moved = await db.Captures.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(row => row.Id == seed.ChurchA.Capture.Id);
                await Assert.That(moved.ChurchId).IsEqualTo(seed.ChurchB.Church.Id);
                await Assert.That(moved.Status).IsEqualTo("provider-control");
                await transaction.RollbackAsync();
            });
        }

        await AssertCapturesAsync(seed);
    }

    [Test]
    public async Task ExecuteUpdateAsync_MigratedUnionAliasWithoutGuard_ProviderSupportsOwnership()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext configured = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using ApplicationDbContext db = CreateProviderControl(configured, seed.ChurchA.Church.Id);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var query = UnionCaptures(db).Select(row => new { Owner = row.ChurchId });

                await Assert.That(await query.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Owner, seed.ChurchB.Church.Id))).IsEqualTo(1);

                DomainCapture moved = await db.Captures.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(row => row.Id == seed.ChurchA.Capture.Id);
                await Assert.That(moved.ChurchId).IsEqualTo(seed.ChurchB.Church.Id);
                await transaction.RollbackAsync();
            });
        }

        await AssertCapturesAsync(seed);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdate_MigratedCompositeSourceOwnership_ThrowsBeforeMutation(bool useUnion, bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            IQueryable<DomainCapture> query = useUnion ? UnionCaptures(db) : CorrelatedCaptures(db);

            await AssertImmutableAsync(() => UpdateAsync(query,
                setters => setters.SetProperty(row => row.ChurchId, seed.ChurchB.Church.Id), useAsync));
        }

        await AssertCapturesAsync(seed);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdate_MigratedCorrelatedOwnershipAlias_ThrowsBeforeMutation(bool useInner, bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var query = useInner
                ? db.Captures.SelectMany(outer => db.Captures.Where(inner => inner.Id == outer.Id),
                    (outer, inner) => new { Owner = inner.ChurchId, ChurchId = inner.Status })
                : db.Captures.SelectMany(outer => db.Captures.Where(inner => inner.Id == outer.Id),
                    (outer, inner) => new { Owner = outer.ChurchId, ChurchId = outer.Status });

            await AssertImmutableAsync(() => UpdateAsync(query,
                setters => setters.SetProperty(row => row.Owner, seed.ChurchB.Church.Id), useAsync));
        }

        await AssertCapturesAsync(seed);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdate_MigratedCorrelatedPayloadAlias_OnlyUpdatesCurrentChurchRows(bool useInner, bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string updatedStatus = """{"ChurchId":"ordinary payload"}""";
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var query = useInner
                ? db.Captures.SelectMany(outer => db.Captures.Where(inner => inner.Id == outer.Id),
                    (outer, inner) => new { ChurchId = inner.Status })
                : db.Captures.SelectMany(outer => db.Captures.Where(inner => inner.Id == outer.Id),
                    (outer, inner) => new { ChurchId = outer.Status });

            int affected = await UpdateAsync(query,
                setters => setters.SetProperty(row => row.ChurchId, updatedStatus), useAsync);
            await Assert.That(affected).IsEqualTo(1);
        }

        await AssertCapturesAsync(seed, updatedStatus);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_MigratedUnionOwnershipProjection_ThrowsBeforeMutation(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var query = UnionCaptures(db).Select(row => new { Owner = row.ChurchId });

            await AssertImmutableAsync(() => UpdateAsync(query,
                setters => setters.SetProperty(row => row.Owner, seed.ChurchB.Church.Id), useAsync));
        }

        await AssertCapturesAsync(seed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_MigratedUnionPayloadAlias_OnlyUpdatesCurrentChurchRows(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string updatedStatus = """{"ChurchId":"ordinary union payload"}""";
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var query = UnionCaptures(db).Select(row => new { ChurchId = row.Status });

            int affected = await UpdateAsync(query,
                setters => setters.SetProperty(row => row.ChurchId, updatedStatus), useAsync);
            await Assert.That(affected).IsEqualTo(1);
        }

        await AssertCapturesAsync(seed, updatedStatus);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdate_WarmedSourceAfterTenantChange_RejectsOwnershipAssignment(bool useUnion, bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            TenantContext tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
            IQueryable<DomainCapture> query = useUnion ? UnionCaptures(db) : CorrelatedCaptures(db);
            string propertyName = nameof(DomainCapture.Id);
            Action<UpdateSettersBuilder<DomainCapture>> setters = setters => setters.SetProperty(
                row => EF.Property<Guid>(row, propertyName), row => EF.Property<Guid>(row, propertyName));

            await Assert.That(await UpdateAsync(query, setters, useAsync)).IsEqualTo(1);
            tenant.ChurchId = seed.ChurchB.Church.Id;
            await Assert.That(await UpdateAsync(query, setters, useAsync)).IsEqualTo(1);
            propertyName = nameof(DomainCapture.ChurchId);

            await AssertImmutableAsync(() => UpdateAsync(query, setters, useAsync));
            tenant.ChurchId = seed.ChurchA.Church.Id;
            await AssertImmutableAsync(() => UpdateAsync(query, setters, useAsync));
        }

        await AssertCapturesAsync(seed);
    }

    private static ApplicationDbContext CreateProviderControl(ApplicationDbContext configured, Guid churchId)
    {
        DbContextOptionsBuilder<ApplicationDbContext> builder = new();
        foreach (IDbContextOptionsExtension extension in configured.GetService<IDbContextOptions>().Extensions
            .Where(extension => extension is not CoreOptionsExtension))
        {
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }

        builder.UseModel(configured.Model);
        return new ApplicationDbContext(builder.Options, new TenantContext { ChurchId = churchId });
    }

    private static IQueryable<DomainCapture> UnionCaptures(ApplicationDbContext db) =>
        db.Captures.Where(row => row.Status != string.Empty)
            .Union(db.Captures.Where(row => row.RawText != null));

    private static IQueryable<DomainCapture> CorrelatedCaptures(ApplicationDbContext db) =>
        db.Captures.SelectMany(outer => db.Captures.Where(inner => inner.Id == outer.Id),
            (outer, inner) => outer);

    private static IQueryable<DomainCapture> TypeOperatorCaptures(ApplicationDbContext db, bool useCast) =>
        useCast
            ? db.Captures.Cast<object>()
                .Where(row => EF.Property<string>(row, nameof(DomainCapture.Status)) != string.Empty)
                .Cast<DomainCapture>()
            : db.Captures.OfType<DomainCapture>();

    private static Task<int> UpdateAsync<TEntity>(
        IQueryable<TEntity> query, Action<UpdateSettersBuilder<TEntity>> setters, bool useAsync) =>
        useAsync ? query.ExecuteUpdateAsync(setters) : Task.FromResult(query.ExecuteUpdate(setters));

    private static async Task AssertImmutableAsync(Func<Task<int>> update) =>
        await Assert.That(async () => await update())
            .Throws<InvalidOperationException>()
            .WithMessage("ChurchId is immutable and cannot be assigned by ExecuteUpdate.");

    private async Task AssertCapturesAsync(ProductionTwoChurchSeed seed, string? updatedStatus = null)
    {
        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using AsyncServiceScope scope = Sql.CreateScope(church.Church.Id);
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            DomainCapture persisted = await db.Captures.AsNoTracking().SingleAsync();

            await Assert.That(persisted.Id).IsEqualTo(church.Capture.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Church.Id);
            await Assert.That(persisted.RawText).IsEqualTo(church.Capture.RawText);
            await Assert.That(persisted.Transcript).IsEqualTo(church.Capture.Transcript);
            await Assert.That(persisted.ExtractionJson).IsEqualTo(church.Capture.ExtractionJson);
            await Assert.That(persisted.Status).IsEqualTo(
                church == seed.ChurchA && updatedStatus is not null ? updatedStatus : church.Capture.Status);
        }
    }
}
