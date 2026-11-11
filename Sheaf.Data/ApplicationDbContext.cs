using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Sheaf.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, Tenancy.ITenantContext
{
    private readonly ICurrentChurch _currentChurch;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : this(options, new TenantContext())
    {
    }

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentChurch currentChurch)
        : base(options)
    {
        _currentChurch = currentChurch;
    }

    public DbSet<Church> Churches => Set<Church>();

    public DbSet<ChurchMembership> ChurchMemberships => Set<ChurchMembership>();

    public DbSet<Context> Contexts => Set<Context>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Capture> Captures => Set<Capture>();

    public DbSet<Touchpoint> Touchpoints => Set<Touchpoint>();

    public DbSet<Proposal> Proposals => Set<Proposal>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<LeaderToken> LeaderTokens => Set<LeaderToken>();

    public DbSet<Relationship> Relationships => Set<Relationship>();

    public DbSet<ContextLeader> ContextLeaders => Set<ContextLeader>();

    public DbSet<ContextMember> ContextMembers => Set<ContextMember>();

    public DbSet<Attendance> Attendances => Set<Attendance>();

    public DbSet<PersonExternalRef> PersonExternalRefs => Set<PersonExternalRef>();

    public Guid ChurchIdOrThrow => _currentChurch.ChurchId
        ?? throw new InvalidOperationException("A church tenant is required for this query.");

    Guid Tenancy.ITenantContext.ChurchId => ChurchIdOrThrow;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.Options.FindExtension<Tenancy.MultiTenancyExtension>() is null)
        {
            throw new InvalidOperationException(
                "ApplicationDbContext requires UseMultiTenancy. Configure production services with AddSheafDatabase.");
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ChurchMembership>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(membership => membership.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });

        DomainModelConfiguration.Configure(builder);

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // SQLite does not have proper support for DateTimeOffset via Entity Framework Core, see the limitations
            // here: https://docs.microsoft.com/ef/core/providers/sqlite/limitations#query-limitations
            // To work around this, when the Sqlite database provider is used, all model properties of type DateTimeOffset
            // use the DateTimeOffsetToBinaryConverter
            // Based on: https://github.com/aspnet/EntityFrameworkCore/issues/10784#issuecomment-415769754
            // This only supports millisecond precision, but should be sufficient for most use cases.
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                var properties = entityType.ClrType
                    .GetProperties()
                    .Where(p => p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?));
                foreach (var property in properties)
                {
                    var propertyBuilder = builder
                        .Entity(entityType.Name)
                        .Property(property.Name)
                        .HasConversion<DateTimeOffsetToBinaryConverter>();
                }
            }

            builder.Entity<IdentityPasskeyData>().HasNoKey();
        }
    }
}
