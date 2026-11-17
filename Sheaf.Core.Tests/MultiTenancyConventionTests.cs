using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

public class MultiTenancyConventionTests
{
    [Test]
    public async Task ProcessModelFinalizing_NonTenantedForeignKeyToPrimaryKey_ThrowsWithBothEntityNames()
    {
        using PrimaryKeyContext context = new(CreateOptions<PrimaryKeyContext>());

        InvalidOperationException exception = await Assert.That(() => context.Model)
            .Throws<InvalidOperationException>()
            ?? throw new InvalidOperationException("Expected model finalization to reject the relationship.");

        await Assert.That(exception.Message.Contains(nameof(PrimaryKeyDependent))).IsTrue();
        await Assert.That(exception.Message.Contains(nameof(PrimaryKeyPrincipal))).IsTrue();
    }

    [Test]
    public async Task ProcessModelFinalizing_NonTenantedForeignKeyToAlternateKey_ThrowsWithBothEntityNames()
    {
        using AlternateKeyContext context = new(CreateOptions<AlternateKeyContext>());

        InvalidOperationException exception = await Assert.That(() => context.Model)
            .Throws<InvalidOperationException>()
            ?? throw new InvalidOperationException("Expected model finalization to reject the relationship.");

        await Assert.That(exception.Message.Contains(nameof(AlternateKeyDependent))).IsTrue();
        await Assert.That(exception.Message.Contains(nameof(AlternateKeyPrincipal))).IsTrue();
    }

    [Test]
    public async Task ProcessModelFinalizing_ImplicitManyToManyJoinToTenantedEntity_ThrowsWithBothEntityNames()
    {
        using ManyToManyContext context = new(CreateOptions<ManyToManyContext>());

        InvalidOperationException exception = await Assert.That(() => context.Model)
            .Throws<InvalidOperationException>()
            ?? throw new InvalidOperationException("Expected model finalization to reject the relationship.");

        await Assert.That(exception.Message.Contains(nameof(NonTenantedManyToManyEntity))).IsTrue();
        await Assert.That(exception.Message.Contains(nameof(TenantedManyToManyEntity))).IsTrue();
    }

    [Test]
    public async Task ProcessModelFinalizing_OwnedDependentOfTenantedEntity_AllowsOwnership()
    {
        DbContextOptions<OwnershipContext> options = new DbContextOptionsBuilder<OwnershipContext>()
            .UseInMemoryDatabase(nameof(ProcessModelFinalizing_OwnedDependentOfTenantedEntity_AllowsOwnership))
            .UseMultiTenancy<OwnershipContext>()
            .Options;
        using OwnershipContext context = new(options);

        IEntityType owner = context.Model.FindEntityType(typeof(TenantedOwner))
            ?? throw new InvalidOperationException("The tenanted owner is missing from the model.");
        IEntityType owned = context.Model.FindEntityType(typeof(OwnedDetails))
            ?? throw new InvalidOperationException("The owned dependent is missing from the model.");

        await Assert.That(owned.FindOwnership()?.PrincipalEntityType).IsEqualTo(owner);
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext, ITenantContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlite("DataSource=:memory:")
            .UseMultiTenancy<TContext>()
            .Options;

    private sealed class PrimaryKeyContext(DbContextOptions<PrimaryKeyContext> options)
        : DbContext(options), ITenantContext
    {
        public Guid ChurchId => Guid.Empty;

        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.Entity<PrimaryKeyDependent>()
                .HasOne(dependent => dependent.Principal)
                .WithMany()
                .HasForeignKey(dependent => dependent.PrincipalId);
    }

    private sealed class PrimaryKeyPrincipal : ITenanted
    {
        public int Id { get; set; }

        public Guid ChurchId { get; set; }
    }

    private sealed class PrimaryKeyDependent
    {
        public int Id { get; set; }

        public int PrincipalId { get; set; }

        public PrimaryKeyPrincipal? Principal { get; set; }
    }

    private sealed class AlternateKeyContext(DbContextOptions<AlternateKeyContext> options)
        : DbContext(options), ITenantContext
    {
        public Guid ChurchId => Guid.Empty;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<AlternateKeyPrincipal>().HasAlternateKey(principal => principal.Code);
            builder.Entity<AlternateKeyDependent>()
                .HasOne(dependent => dependent.Principal)
                .WithMany()
                .HasForeignKey(dependent => dependent.PrincipalCode)
                .HasPrincipalKey(principal => principal.Code);
        }
    }

    private sealed class AlternateKeyPrincipal : ITenanted
    {
        public int Id { get; set; }

        public Guid ChurchId { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    private sealed class AlternateKeyDependent
    {
        public int Id { get; set; }

        public string PrincipalCode { get; set; } = string.Empty;

        public AlternateKeyPrincipal? Principal { get; set; }
    }

    private sealed class ManyToManyContext(DbContextOptions<ManyToManyContext> options)
        : DbContext(options), ITenantContext
    {
        public Guid ChurchId => Guid.Empty;

        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.Entity<NonTenantedManyToManyEntity>()
                .HasMany(entity => entity.TenantedEntities)
                .WithMany(entity => entity.NonTenantedEntities);
    }

    private sealed class NonTenantedManyToManyEntity
    {
        public int Id { get; set; }

        public ICollection<TenantedManyToManyEntity> TenantedEntities { get; set; } = [];
    }

    private sealed class TenantedManyToManyEntity : ITenanted
    {
        public int Id { get; set; }

        public Guid ChurchId { get; set; }

        public ICollection<NonTenantedManyToManyEntity> NonTenantedEntities { get; set; } = [];
    }

    private sealed class OwnershipContext(DbContextOptions<OwnershipContext> options)
        : DbContext(options), ITenantContext
    {
        public Guid ChurchId => Guid.Empty;

        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.Entity<TenantedOwner>().OwnsOne(owner => owner.Details);
    }

    private sealed class TenantedOwner : ITenanted
    {
        public int Id { get; set; }

        public Guid ChurchId { get; set; }

        public OwnedDetails Details { get; set; } = new();
    }

    private sealed class OwnedDetails
    {
        public string Value { get; set; } = string.Empty;
    }
}
