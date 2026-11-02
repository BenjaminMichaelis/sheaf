using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

using Church = Sheaf.Data.Church;

namespace Sheaf.Core.Tests;

public sealed class Leader : ITenanted
{
    public Guid Id { get; set; }
    public Guid ChurchId { get; set; }
    public required string Name { get; set; }
}

public sealed class Person : ITenanted
{
    public Guid Id { get; set; }
    public Guid ChurchId { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public Guid? LeaderId { get; set; }
    public Leader? Leader { get; set; }
}

/// <summary>A tenanted model for exercising the tenancy convention against real providers.</summary>
public sealed class TenancyTestDbContext(DbContextOptions<TenancyTestDbContext> options, Guid? churchId)
    : DbContext(options), ITenantContext
{
    Guid ITenantContext.ChurchId => churchId
        ?? throw new InvalidOperationException("A church tenant is required for this query.");

    public DbSet<Church> Churches => Set<Church>();
    public DbSet<Leader> Leaders => Set<Leader>();
    public DbSet<Person> People => Set<Person>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The Person-to-Leader relationship is left to EF conventions: an explicit foreign key
        // configuration would prevent the tenancy convention from adding ChurchId to it.
        modelBuilder.Entity<Person>().HasIndex(p => new { p.ChurchId, p.Email }).IsUnique();
    }

    public static DbContextOptions<TenancyTestDbContext> BuildOptions(Action<DbContextOptionsBuilder<TenancyTestDbContext>> useProvider)
    {
        DbContextOptionsBuilder<TenancyTestDbContext> builder = new();
        useProvider(builder);
        return builder.UseMultiTenancy().Options;
    }
}
