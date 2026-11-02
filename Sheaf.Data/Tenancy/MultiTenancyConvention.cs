using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Sheaf.Data.Tenancy;

/// <summary>
/// For every <see cref="ITenanted"/> entity: adds the <c>TenancyFilter</c> named query filter, makes the primary key
/// <c>(ChurchId, Id)</c>, and rewires foreign keys to tenanted principals to <c>(ChurchId, FkId)</c>.
/// </summary>
public sealed class MultiTenancyConvention(Type contextType) : IModelFinalizingConvention
{
    public const string FilterName = "TenancyFilter";
    public const string ChurchIdName = nameof(ITenanted.ChurchId);

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        List<IConventionEntityType> tenanted = modelBuilder.Metadata.GetEntityTypes()
            .Where(e => !e.IsOwned() && typeof(ITenanted).IsAssignableFrom(e.ClrType))
            .ToList();

        Dictionary<IConventionEntityType, IConventionKey> oldKeys = new();

        foreach (IConventionEntityType entityType in tenanted.Where(e => e.BaseType is null))
        {
            IConventionProperty churchId = entityType.Builder.Property(typeof(Guid), ChurchIdName)!.Metadata;
            entityType.Builder.Property(typeof(Guid), ChurchIdName)!.IsRequired(true);

            IConventionKey? primaryKey = entityType.FindPrimaryKey();
            IReadOnlyList<IConventionProperty> idProperties = primaryKey?.Properties
                ?? (entityType.FindProperty("Id") is { } id ? [id] : []);
            if (idProperties.Count > 0 && !idProperties.Contains(churchId))
            {
                if (primaryKey is not null)
                {
                    oldKeys[entityType] = primaryKey;
                }

                entityType.SetPrimaryKey([churchId, .. idProperties]);
            }

            entityType.SetQueryFilter(FilterName, BuildFilter(entityType.ClrType));
        }

        foreach (IConventionEntityType dependent in tenanted)
        {
            IConventionProperty churchId = dependent.FindProperty(ChurchIdName)!;
            foreach (IConventionForeignKey fk in dependent.GetDeclaredForeignKeys().ToList())
            {
                IConventionEntityType principal = fk.PrincipalEntityType;
                if (fk.Properties.Contains(churchId)
                    || !tenanted.Contains(principal)
                    || principal.FindPrimaryKey() is not { } principalKey
                    || !principalKey.Properties.Contains(principal.FindProperty(ChurchIdName)))
                {
                    continue;
                }

                IConventionKey oldPrincipalKey = fk.PrincipalKey;
                IConventionKey newPrincipalKey = principalKey;
                if (oldPrincipalKey == newPrincipalKey)
                {
                    continue;
                }

                IConventionForeignKey? rewired = fk.Builder.HasForeignKey([churchId, .. fk.Properties])?.Metadata;
                if (rewired is null)
                {
                    continue;
                }

                rewired.Builder.HasPrincipalKey(newPrincipalKey.Properties);
            }
        }

        foreach ((IConventionEntityType entityType, IConventionKey key) in oldKeys)
        {
            if (!key.GetReferencingForeignKeys().Any())
            {
                entityType.RemoveKey(key);
            }
        }
    }

    // ctx.ChurchId is rewritten by EF to the current context instance when the query is compiled.
    private LambdaExpression BuildFilter(Type entityClrType)
    {
        ParameterExpression entity = Expression.Parameter(entityClrType, "e");
        MemberExpression entityChurchId = Expression.Property(
            Expression.Convert(entity, typeof(ITenanted)), nameof(ITenanted.ChurchId));
        MemberExpression currentChurchId = Expression.Property(
            Expression.Convert(Expression.Constant(null, contextType), typeof(ITenantContext)),
            nameof(ITenantContext.ChurchId));
        return Expression.Lambda(Expression.Equal(entityChurchId, currentChurchId), entity);
    }
}
