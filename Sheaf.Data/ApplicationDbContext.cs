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

    public DbSet<GroupType> GroupTypes => Set<GroupType>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Household> Households => Set<Household>();

    public DbSet<Context> Contexts => Set<Context>();

    public DbSet<ContextMember> ContextMembers => Set<ContextMember>();

    public DbSet<ContextLeader> ContextLeaders => Set<ContextLeader>();

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

        builder.Entity<GroupType>(entity =>
        {
            entity.HasOne<Church>().WithMany().HasForeignKey(type => type.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.ToTable(table => table.HasCheckConstraint("CK_GroupTypes_Name", "TRIM([Name]) <> ''"));
        });

        builder.Entity<Person>(entity =>
        {
            entity.HasOne<Church>().WithMany().HasForeignKey(person => person.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<Household>().WithMany().HasForeignKey(person => person.HouseholdId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(person => person.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(person => new { person.ChurchId, person.UserId }).IsUnique()
                .HasFilter("[UserId] IS NOT NULL");
            entity.ToTable(table => table.HasCheckConstraint("CK_People_Name", "TRIM([Name]) <> ''"));
        });
        builder.Entity<Household>().HasOne<Church>().WithMany().HasForeignKey(household => household.ChurchId)
            .OnDelete(DeleteBehavior.Restrict).IsRequired();
        builder.Entity<Context>(entity =>
        {
            entity.HasOne<Church>().WithMany().HasForeignKey(group => group.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<GroupType>().WithMany().HasForeignKey(group => group.GroupTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Contexts_Name", "TRIM([Name]) <> ''");
                table.HasCheckConstraint("CK_Contexts_Dates", "[EndsAt] IS NULL OR [EndsAt] >= [StartsAt]");
                table.HasCheckConstraint("CK_Contexts_Kind", "[Kind] = 0");
            });
        });
        builder.Entity<ContextMember>(entity =>
        {
            entity.HasOne<Church>().WithMany().HasForeignKey(member => member.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<Context>().WithMany().HasForeignKey(member => member.ContextId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany().HasForeignKey(member => member.PersonId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("CK_ContextMembers_Dates", "[LeftAt] IS NULL OR [LeftAt] >= [JoinedAt]"));
        });
        builder.Entity<ContextLeader>(entity =>
        {
            entity.HasOne<Church>().WithMany().HasForeignKey(leader => leader.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<Context>().WithMany().HasForeignKey(leader => leader.ContextId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany().HasForeignKey(leader => leader.PersonId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("CK_ContextLeaders_Dates", "[EndsAt] IS NULL OR [EndsAt] >= [StartsAt]"));
        });

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
