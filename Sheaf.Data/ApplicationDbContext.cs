using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Sheaf.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, Tenancy.ITenantContext, IDataProtectionKeyContext
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

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupAssignment> GroupAssignments => Set<GroupAssignment>();

    public DbSet<GroupMeeting> GroupMeetings => Set<GroupMeeting>();

    public DbSet<Attendance> Attendances => Set<Attendance>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

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
            entity.Property(membership => membership.Role).IsConcurrencyToken();
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

        builder.Entity<Church>().ToTable(table => table.HasCheckConstraint("CK_Churches_TimeZoneId", "TRIM([TimeZoneId]) <> ''"));

        builder.Entity<GroupType>(entity =>
        {
            entity.HasOne<Church>().WithMany().HasForeignKey(type => type.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.ToTable(table => table.HasCheckConstraint("CK_GroupTypes_Name", "TRIM([Name]) <> ''"));
        });

        builder.Entity<Person>(entity =>
        {
            // Imports and direct inserts that omit CreatedAt still get a creation time.
            entity.Property(person => person.CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
            entity.HasOne<Church>().WithMany().HasForeignKey(person => person.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<Household>().WithMany().HasForeignKey(person => person.HouseholdId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(person => person.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(person => new { person.ChurchId, person.UserId }).IsUnique()
                .HasFilter("[UserId] IS NOT NULL");
            entity.ToTable(table => table.HasCheckConstraint("CK_People_FirstName", "TRIM([FirstName]) <> ''"));
        });
        builder.Entity<Household>().HasOne<Church>().WithMany().HasForeignKey(household => household.ChurchId)
            .OnDelete(DeleteBehavior.Restrict).IsRequired();
        builder.Entity<Group>(entity =>
        {
            entity.HasOne<Church>().WithMany().HasForeignKey(group => group.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<GroupType>().WithMany().HasForeignKey(group => group.GroupTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Groups_Name", "TRIM([Name]) <> ''");
                table.HasCheckConstraint("CK_Groups_Dates", "[EndsAt] IS NULL OR [EndsAt] >= [StartsAt]");
            });
        });
        builder.Entity<GroupAssignment>(entity =>
        {
            entity.HasOne<Church>().WithMany().HasForeignKey(assignment => assignment.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<Group>().WithMany().HasForeignKey(assignment => assignment.GroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany().HasForeignKey(assignment => assignment.PersonId).OnDelete(DeleteBehavior.Restrict);
            // At most one open assignment per person and group, even under concurrent writes. The service also rejects
            // overlapping intervals that are already closed.
            entity.HasIndex(assignment => new { assignment.ChurchId, assignment.GroupId, assignment.PersonId }).IsUnique()
                .HasFilter("[EndsAt] IS NULL");
            // Access checks ask which groups the caller's person is assigned to.
            entity.HasIndex(assignment => new { assignment.ChurchId, assignment.PersonId });
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_GroupAssignments_Dates", "[EndsAt] IS NULL OR [EndsAt] >= [StartsAt]");
                table.HasCheckConstraint("CK_GroupAssignments_Role", "[Role] IN (0, 1)");
            });
        });
        builder.Entity<GroupMeeting>(entity =>
        {
            entity.Property(meeting => meeting.RecordedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
            entity.HasOne<Church>().WithMany().HasForeignKey(meeting => meeting.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<Group>().WithMany().HasForeignKey(meeting => meeting.GroupId).OnDelete(DeleteBehavior.Restrict);
            // A group's meetings are listed newest first; more than one per date is allowed.
            entity.HasIndex(meeting => new { meeting.ChurchId, meeting.GroupId, meeting.OccurredOn });
        });
        builder.Entity<Attendance>(entity =>
        {
            entity.Property(attendance => attendance.RecordedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
            entity.HasOne<Church>().WithMany().HasForeignKey(attendance => attendance.ChurchId)
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne<GroupMeeting>().WithMany().HasForeignKey(attendance => attendance.MeetingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany().HasForeignKey(attendance => attendance.PersonId).OnDelete(DeleteBehavior.Restrict);
            // A person counts once per meeting; removed rows stay as history and do not count.
            entity.HasIndex(attendance => new { attendance.ChurchId, attendance.MeetingId, attendance.PersonId }).IsUnique()
                .HasFilter("[RemovedAt] IS NULL");
            // Profile visibility asks which sessions a person attended recently.
            entity.HasIndex(attendance => new { attendance.ChurchId, attendance.PersonId });
            entity.ToTable(table => table.HasCheckConstraint("CK_Attendances_Removed",
                "([RemovedAt] IS NULL AND [RemovedByUserId] IS NULL) OR ([RemovedAt] IS NOT NULL AND [RemovedAt] >= [RecordedAt])"));
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
