using System.Linq.Expressions;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Sheaf.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, Tenancy.ITenantContext
{
    private readonly ITenantContext _tenantContext;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : this(options, new TenantContext())
    {
    }

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Church> Churches => Set<Church>();

    public Guid ChurchIdOrThrow => _tenantContext.ChurchId
        ?? throw new InvalidOperationException("A church tenant is required for this query.");

    Guid Tenancy.ITenantContext.ChurchId => ChurchIdOrThrow;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(new Tenancy.TenancySaveChangesInterceptor());

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(entityType => typeof(ITenanted).IsAssignableFrom(entityType.ClrType)))
        {
            var entity = Expression.Parameter(entityType.ClrType, "entity");
            var entityChurchId = Expression.Property(entity, nameof(ITenanted.ChurchId));
            var currentChurchId = Expression.Property(
                Expression.Constant(this),
                nameof(ChurchIdOrThrow));
            var filter = Expression.Lambda(
                Expression.Equal(entityChurchId, currentChurchId),
                entity);

            builder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }

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
