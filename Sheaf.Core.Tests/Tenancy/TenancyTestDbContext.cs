using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

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

/// <summary>A global principal whose collection navigation reaches tenanted rows.</summary>
public sealed class Hub
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public List<Spoke> Spokes { get; set; } = [];
}

public sealed class Spoke : ITenanted
{
    public Guid Id { get; set; }
    public Guid ChurchId { get; set; }
    public required string Name { get; set; }
    public Guid HubId { get; set; }
}

public sealed class GuardRecord : ITenanted
{
    public Guid Id { get; set; }
    public Guid ChurchId { get; set; }
    public required string Name { get; set; }
    public GuardRecordDetails Details { get; set; } = new();
}

public sealed class GuardRecordDetails
{
    public string Label { get; set; } = string.Empty;
    public List<GuardRecordItem> Items { get; set; } = [];
}

public sealed class GuardRecordItem
{
    public string Value { get; set; } = string.Empty;
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
    public DbSet<Hub> Hubs => Set<Hub>();
    public DbSet<Spoke> Spokes => Set<Spoke>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>().HasIndex(p => new { p.ChurchId, p.Email }).IsUnique();
        modelBuilder.Entity<Leader>()
            .HasOne<Church>()
            .WithMany()
            .HasForeignKey(leader => leader.ChurchId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        modelBuilder.Entity<Person>()
            .HasOne<Church>()
            .WithMany()
            .HasForeignKey(person => person.ChurchId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        modelBuilder.Entity<Spoke>()
            .HasOne<Church>()
            .WithMany()
            .HasForeignKey(spoke => spoke.ChurchId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        modelBuilder.Entity<GuardRecord>(entity =>
        {
            entity.HasKey(record => new { record.ChurchId, record.Id });
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(record => record.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.OwnsOne(record => record.Details, details =>
            {
                details.ToJson();
                details.OwnsMany(value => value.Items);
            });
        });
    }

    public static DbContextOptions<TenancyTestDbContext> BuildOptions(Action<DbContextOptionsBuilder<TenancyTestDbContext>> useProvider)
    {
        DbContextOptionsBuilder<TenancyTestDbContext> builder = new();
        useProvider(builder);
        return builder.UseMultiTenancy().Options;
    }
}

public sealed class TenancyTestDbContextFactory : IDesignTimeDbContextFactory<TenancyTestDbContext>
{
    public TenancyTestDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<TenancyTestDbContext> options = new DbContextOptionsBuilder<TenancyTestDbContext>()
            .UseAzureSql("Server=(localdb)\\mssqllocaldb;Database=SheafTenancyTestMigrations;TrustServerCertificate=True")
            .UseMultiTenancy()
            .Options;

        return new TenancyTestDbContext(options, churchId: null);
    }
}
