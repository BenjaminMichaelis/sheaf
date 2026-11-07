using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheaf.Core;
using Sheaf.Data;
using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

internal static class ApplicationDbContextProbe
{
    public static IHost CreateHost()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Production
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{ConnectionStrings.DatabaseKey}"] =
                "Server=(localdb)\\mssqllocaldb;Database=SheafTenancyModelTests;Integrated Security=True;TrustServerCertificate=True"
        });
        builder.AddDatabase();
        return builder.Build();
    }
}

internal class TenantTestDbContext : ApplicationDbContext
{
    public TenantTestDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public TenantTestDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentChurch currentChurch)
        : base(options, currentChurch)
    {
    }

    public DbSet<TenantRecord> TenantRecords => Set<TenantRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<TenantRecord>();
        base.OnModelCreating(builder);

        builder.Entity<TenantRecord>()
            .HasOne<Church>()
            .WithMany()
            .HasForeignKey(record => record.ChurchId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}

internal class TenantRecord : ITenanted
{
    public int Id { get; set; }
    public Guid ChurchId { get; set; }
}

internal class TenancyArchitectureDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentChurch currentChurch) : TenantTestDbContext(options, currentChurch)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TenantPrincipalProbe>(entity =>
        {
            // JSON ownership must reference the owner's full tenant key.
            entity.HasKey(principal => new { principal.ChurchId, principal.Id });
            entity.HasAlternateKey(principal => new { principal.ChurchId, principal.Code });
            entity.HasIndex(principal => new { principal.ChurchId, principal.UserId }).IsUnique();
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(principal => principal.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(principal => principal.UserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(principal => principal.Guardian)
                .WithMany()
                .HasForeignKey(principal => new { principal.ChurchId, principal.GuardianId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.OwnsOne(principal => principal.Fields, owned => owned.ToJson());
        });

        builder.Entity<DerivedTenantRecord>(entity =>
        {
            entity.HasIndex(record => new { record.ChurchId, record.Detail }).IsUnique();
            entity.HasOne<TenantPrincipalProbe>()
                .WithMany()
                .HasForeignKey(record => new { record.ChurchId, record.PrincipalId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TenantJoinProbe>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(join => join.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne(join => join.Record)
                .WithMany()
                .HasForeignKey(join => join.RecordId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(join => join.Principal)
                .WithMany()
                .HasForeignKey(join => new { join.ChurchId, join.PrincipalId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class DerivedTenantRecord : TenantRecord
{
    public string Detail { get; set; } = string.Empty;
    public Guid PrincipalId { get; set; }
}

internal sealed class TenantPrincipalProbe : ITenanted
{
    public Guid Id { get; set; }
    public Guid ChurchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public Guid? GuardianId { get; set; }
    public TenantPrincipalProbe? Guardian { get; set; }
    public TenantJsonProbe Fields { get; set; } = new();
}

internal sealed class TenantJsonProbe
{
    public string Caption { get; set; } = string.Empty;
}

internal sealed class TenantJoinProbe : ITenanted
{
    public Guid Id { get; set; }
    public Guid ChurchId { get; set; }
    public int RecordId { get; set; }
    public TenantRecord? Record { get; set; }
    public Guid PrincipalId { get; set; }
    public TenantPrincipalProbe? Principal { get; set; }
}

internal sealed class TenancyArchitectureFixture : IDisposable
{
    private readonly IHost _host = ApplicationDbContextProbe.CreateHost();
    private readonly IServiceScope _scope;

    public TenancyArchitectureFixture()
    {
        _scope = _host.Services.CreateScope();
        RuntimeContext = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Options = _scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        ProbeContext = new TenancyArchitectureDbContext(
            Options,
            _scope.ServiceProvider.GetRequiredService<ICurrentChurch>());
    }

    public ApplicationDbContext RuntimeContext { get; }
    public DbContextOptions<ApplicationDbContext> Options { get; }
    public TenancyArchitectureDbContext ProbeContext { get; }

    public void Dispose()
    {
        ProbeContext.Dispose();
        _scope.Dispose();
        _host.Dispose();
    }
}
