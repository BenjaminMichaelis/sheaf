using System.Linq.Expressions;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data.Security;

/// <summary>
/// Church membership and role are authorization inputs carried by Identity cookies. Removing a membership or
/// changing its role rotates the affected user's security stamp in the same save, and bulk writes that would
/// bypass the rotation are rejected.
/// </summary>
public sealed partial class MembershipSecurityStampInterceptor : SaveChangesInterceptor, IQueryExpressionInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<MembershipChange>> _pending = [];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            foreach (string userId in Prepare(context))
            {
                if (context.Find<ApplicationUser>(userId) is { } user)
                {
                    Rotate(context, user);
                }
            }
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            foreach (string userId in Prepare(context))
            {
                if (await context.FindAsync<ApplicationUser>([userId], cancellationToken) is { } user)
                {
                    Rotate(context, user);
                }
            }
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Audit(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Audit(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Discard(eventData.Context);

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => Discard(eventData.Context);

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        if (queryExpression is MethodCallExpression { Method: { DeclaringType: { } declaringType } method } bulk
            && declaringType == typeof(EntityFrameworkQueryableExtensions)
            && method.IsGenericMethod
            && (method.GetGenericArguments()[0] == typeof(ChurchMembership)
                || method.Name == nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)
                    && TargetsMembershipQuery(bulk.Arguments[0])))
        {
            switch (method.Name)
            {
                case nameof(EntityFrameworkQueryableExtensions.ExecuteDelete):
                    throw new InvalidOperationException(
                        "ExecuteDelete cannot remove church memberships; delete tracked entities with SaveChanges "
                        + "so the user's security stamp is rotated.");

                case nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate):
                    RejectAuthorizationUpdates(bulk, eventData.Context?.Model);
                    break;
            }
        }

        return queryExpression;
    }

    private static bool TargetsMembershipQuery(Expression query)
    {
        while (query is MethodCallExpression call
            && call.Method.DeclaringType is { } declaringType
            && (declaringType == typeof(Queryable)
                || declaringType == typeof(EntityFrameworkQueryableExtensions)
                || declaringType == typeof(RelationalQueryableExtensions))
            && call.Arguments.Count > 0)
        {
            if (call.Method.Name is nameof(Queryable.Union) or nameof(Queryable.Concat)
                or nameof(Queryable.Intersect) or nameof(Queryable.Except))
            {
                return TargetsMembershipQuery(call.Arguments[0]) || TargetsMembershipQuery(call.Arguments[1]);
            }

            query = call.Arguments[0];
        }

        return query is EntityQueryRootExpression root
            && root.EntityType.ClrType == typeof(ChurchMembership);
    }

    private static void RejectAuthorizationUpdates(MethodCallExpression bulk, IModel? model)
    {
        if (model is null || bulk.Arguments.Count != 2 || bulk.Arguments[1] is not NewArrayExpression setters)
        {
            throw UnverifiableUpdate();
        }

        foreach (Expression setter in setters.Expressions)
        {
            if (setter is not NewExpression { Arguments.Count: 2 } tuple
                || TenancyQueryExpressionInterceptor.Unwrap(tuple.Arguments[0]) is not LambdaExpression
                { Parameters.Count: 1 } property)
            {
                throw UnverifiableUpdate();
            }

            IProperty mapped;
            try
            {
                (_, mapped) = TenancyQueryExpressionInterceptor.ResolveProperty(
                    model, bulk.Arguments[0], property.Parameters[0], property.Body);
            }
            catch (InvalidOperationException)
            {
                throw UnverifiableUpdate();
            }

            if (mapped.DeclaringType.ClrType == typeof(ChurchMembership)
                && (mapped.Name is nameof(ChurchMembership.Role) or nameof(ChurchMembership.UserId)))
            {
                throw new InvalidOperationException(
                    $"ExecuteUpdate cannot assign ChurchMembership.{mapped.Name}; update tracked entities with "
                    + "SaveChanges so the user's security stamp is rotated.");
            }
        }
    }

    private static InvalidOperationException UnverifiableUpdate() =>
        new("ExecuteUpdate on church memberships cannot verify the mapped setter target; "
            + "membership role changes must rotate the user's security stamp.");

    // A reassigned membership is audited as a removal for the old user and a creation for the new one.
    private string[] Prepare(DbContext context)
    {
        _pending.Remove(context);
        List<MembershipChange> changes = [];
        foreach (EntityEntry<ChurchMembership> entry in context.ChangeTracker.Entries<ChurchMembership>())
        {
            ChurchMembership membership = entry.Entity;
            switch (entry.State)
            {
                case EntityState.Added:
                    changes.Add(new(MembershipChangeKind.Created, membership, membership.UserId, membership.Role,
                        membership.Role));
                    break;

                case EntityState.Deleted:
                    ChurchMembershipRole deletedRole = OriginalRole(entry);
                    changes.Add(new(MembershipChangeKind.Removed, membership, OriginalUserId(entry), deletedRole,
                        deletedRole));
                    break;

                case EntityState.Modified:
                    string originalUserId = OriginalUserId(entry);
                    ChurchMembershipRole originalRole = OriginalRole(entry);
                    if (originalUserId != membership.UserId)
                    {
                        changes.Add(new(MembershipChangeKind.Removed, membership, originalUserId, originalRole,
                            originalRole));
                        changes.Add(new(MembershipChangeKind.Created, membership, membership.UserId, membership.Role,
                            membership.Role));
                    }
                    else if (originalRole != membership.Role)
                    {
                        changes.Add(new(MembershipChangeKind.RoleChanged, membership, membership.UserId, originalRole,
                            membership.Role));
                    }

                    break;
            }
        }

        if (changes.Count == 0)
        {
            return [];
        }

        _pending.Add(context, changes);
        return changes
            .Where(change => change.Kind != MembershipChangeKind.Created)
            .Select(change => change.UserId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string OriginalUserId(EntityEntry<ChurchMembership> entry) =>
        entry.OriginalValues.GetValue<string>(nameof(ChurchMembership.UserId));

    private static ChurchMembershipRole OriginalRole(EntityEntry<ChurchMembership> entry) =>
        entry.OriginalValues.GetValue<ChurchMembershipRole>(nameof(ChurchMembership.Role));

    private static void Rotate(DbContext context, ApplicationUser user)
    {
        EntityEntry<ApplicationUser> entry = context.Entry(user);
        if (entry.State == EntityState.Deleted)
        {
            return;
        }

        user.SecurityStamp = Guid.NewGuid().ToString();
        entry.Property(entity => entity.SecurityStamp).IsModified = true;
    }

    private void Audit(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out List<MembershipChange>? changes))
        {
            return;
        }

        _pending.Remove(context);
        ILogger logger = context.GetService<ILoggerFactory>().CreateLogger<MembershipSecurityStampInterceptor>();
        foreach (MembershipChange change in changes)
        {
            ChurchMembership membership = change.Membership;
            switch (change.Kind)
            {
                case MembershipChangeKind.Created:
                    LogMembershipCreated(logger, membership.ChurchId, membership.Id, change.UserId, change.Role);
                    break;

                case MembershipChangeKind.Removed:
                    LogMembershipRemoved(logger, membership.ChurchId, membership.Id, change.UserId, change.Role);
                    break;

                case MembershipChangeKind.RoleChanged:
                    LogMembershipRoleChanged(logger, membership.ChurchId, membership.Id, change.UserId,
                        change.OriginalRole, change.Role);
                    break;
            }
        }
    }

    private void Discard(DbContext? context)
    {
        if (context is not null)
        {
            _pending.Remove(context);
        }
    }

    [LoggerMessage(EventId = 15601, EventName = "ChurchMembershipCreated", Level = LogLevel.Information,
        Message = "Church membership {MembershipId} created for user {UserId} in church {ChurchId} with role {Role}.")]
    private static partial void LogMembershipCreated(
        ILogger logger, Guid churchId, Guid membershipId, string userId, ChurchMembershipRole role);

    [LoggerMessage(EventId = 15602, EventName = "ChurchMembershipRoleChanged", Level = LogLevel.Information,
        Message = "Church membership {MembershipId} for user {UserId} in church {ChurchId} changed role from {OldRole} to {NewRole}; security stamp rotated.")]
    private static partial void LogMembershipRoleChanged(
        ILogger logger, Guid churchId, Guid membershipId, string userId, ChurchMembershipRole oldRole,
        ChurchMembershipRole newRole);

    [LoggerMessage(EventId = 15603, EventName = "ChurchMembershipRemoved", Level = LogLevel.Information,
        Message = "Church membership {MembershipId} for user {UserId} in church {ChurchId} with role {Role} removed; security stamp rotated.")]
    private static partial void LogMembershipRemoved(
        ILogger logger, Guid churchId, Guid membershipId, string userId, ChurchMembershipRole role);

    private enum MembershipChangeKind
    {
        Created,
        RoleChanged,
        Removed
    }

    private sealed record MembershipChange(
        MembershipChangeKind Kind,
        ChurchMembership Membership,
        string UserId,
        ChurchMembershipRole OriginalRole,
        ChurchMembershipRole Role);
}
