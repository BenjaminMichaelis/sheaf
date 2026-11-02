using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sheaf.Data.Tenancy;

public sealed class TenancySaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateChanges(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateChanges(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void ValidateChanges(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        List<EntityEntry<ITenanted>> entries = context.ChangeTracker.Entries<ITenanted>().ToList();
        if (entries.Count == 0)
        {
            return;
        }

        ITenantContext tenantContext = context as ITenantContext
            ?? throw new InvalidOperationException(
                $"The {nameof(TenancySaveChangesInterceptor)} requires a DbContext that implements {nameof(ITenantContext)}.");
        Guid churchId = tenantContext.ChurchId;
        if (churchId == Guid.Empty)
        {
            throw new InvalidOperationException("A church tenant is required to save tenanted entities.");
        }

        foreach (EntityEntry<ITenanted> entry in entries)
        {
            Guid entityChurchId = entry.Entity.ChurchId;
            PropertyEntry<ITenanted, Guid> churchIdProperty = entry.Property(entity => entity.ChurchId);
            if ((entry.State is EntityState.Modified or EntityState.Deleted)
                && (churchIdProperty.IsModified || churchIdProperty.CurrentValue != churchIdProperty.OriginalValue))
            {
                throw new InvalidOperationException("ChurchId is immutable after an entity is tracked.");
            }

            switch (entry.State)
            {
                case EntityState.Added when entityChurchId == Guid.Empty:
                    entry.Entity.ChurchId = churchId;
                    break;

                case EntityState.Added or EntityState.Modified or EntityState.Deleted
                    when entityChurchId != churchId:
                    throw ForeignChurch(entityChurchId, churchId);
            }
        }
    }

    private static InvalidOperationException ForeignChurch(Guid entityChurchId, Guid currentChurchId) =>
        new($"The entity belongs to church '{entityChurchId}', not the current church '{currentChurchId}'.");
}
