using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

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

        List<EntityEntry> allEntries = context.ChangeTracker.Entries().ToList();
        List<EntityEntry<ITenanted>> entries = context.ChangeTracker.Entries<ITenanted>().ToList();

        // Owned entities (including JSON-mapped ones) are not ITenanted, and the owner can stay Unchanged
        // when only owned data changes, so each changed owned entry is attributed to its root owner.
        // Owners are resolved before Added roots are stamped so key matching uses the tracked values.
        List<(EntityEntry Owned, ITenanted? Owner)> ownedChanges = allEntries
            .Where(entry => entry.Metadata.IsOwned()
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && HasTenantedRoot(entry.Metadata))
            .Select(entry => (entry, FindTenantedRoot(entry, allEntries)))
            .ToList();

        if (entries.Count == 0 && ownedChanges.Count == 0)
        {
            return;
        }

        if (context is not ITenantContext tenantContext)
        {
            throw new InvalidOperationException(
                $"The {nameof(TenancySaveChangesInterceptor)} requires a DbContext that implements {nameof(ITenantContext)}.");
        }

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

        foreach ((EntityEntry owned, ITenanted? owner) in ownedChanges)
        {
            if (owner is null)
            {
                throw new InvalidOperationException(
                    $"The owned entity '{owned.Metadata.DisplayName()}' has no tracked tenanted owner.");
            }

            Guid ownerChurchId = owner.ChurchId;
            if (ownerChurchId != churchId)
            {
                throw ForeignChurch(ownerChurchId, churchId);
            }
        }
    }

    private static bool HasTenantedRoot(IEntityType entityType)
    {
        IEntityType current = entityType;
        while (current.FindOwnership() is { } ownership)
        {
            current = ownership.PrincipalEntityType;
        }

        return typeof(ITenanted).IsAssignableFrom(current.ClrType);
    }

    private static ITenanted? FindTenantedRoot(EntityEntry entry, List<EntityEntry> trackedEntries)
    {
        EntityEntry current = entry;
        while (current.Metadata.FindOwnership() is { } ownership)
        {
            EntityEntry? principal = FindPrincipal(current, ownership, trackedEntries);
            if (principal is null)
            {
                return null;
            }

            current = principal;
        }

        return current.Entity as ITenanted;
    }

    private static EntityEntry? FindPrincipal(
        EntityEntry dependent,
        IForeignKey ownership,
        List<EntityEntry> trackedEntries)
    {
        IReadOnlyList<IProperty> foreignKeyProperties = ownership.Properties;
        IReadOnlyList<IProperty> principalKeyProperties = ownership.PrincipalKey.Properties;
        object?[] foreignKeyValues = foreignKeyProperties
            .Select(property => dependent.Property(property).CurrentValue)
            .ToArray();

        return trackedEntries.FirstOrDefault(candidate =>
            ownership.PrincipalEntityType.IsAssignableFrom(candidate.Metadata)
            && principalKeyProperties
                .Select((property, index) => Equals(candidate.Property(property).CurrentValue, foreignKeyValues[index]))
                .All(matches => matches));
    }

    private static InvalidOperationException ForeignChurch(Guid entityChurchId, Guid currentChurchId) =>
        new($"The entity belongs to church '{entityChurchId}', not the current church '{currentChurchId}'.");
}
