using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Sheaf.Data.Tenancy;

/// <summary>
/// For every <see cref="ITenanted"/> entity: adds the <c>TenancyFilter</c> named query filter, makes the primary key
/// <c>(ChurchId, Id)</c>, and rewires foreign keys to tenanted principals to <c>(ChurchId, FkId)</c>.
/// Non-tenanted dependents cannot reference tenanted principals, except through ownership relationships.
/// Many-to-many relationships between tenanted entities require an explicit <see cref="ITenanted"/> join entity.
/// </summary>
public sealed class MultiTenancyConvention(Type contextType) : IModelFinalizingConvention
{
    public const string FilterName = "TenancyFilter";
    public const string ChurchIdName = nameof(ITenanted.ChurchId);

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        List<IConventionEntityType> entityTypes = modelBuilder.Metadata.GetEntityTypes().ToList();
        foreach (IConventionEntityType dependent in entityTypes.Where(e => !typeof(ITenanted).IsAssignableFrom(e.ClrType)))
        {
            foreach (IConventionForeignKey foreignKey in dependent.GetDeclaredForeignKeys())
            {
                IConventionEntityType principal = foreignKey.PrincipalEntityType;
                if (!foreignKey.IsOwnership && typeof(ITenanted).IsAssignableFrom(principal.ClrType))
                {
                    throw new InvalidOperationException(
                        $"Non-tenanted {dependent.DisplayName()} cannot have a foreign key to tenanted {principal.DisplayName()}. "
                        + "Use an explicit ITenanted dependent; implicit many-to-many joins are not supported.");
                }
            }
        }

        List<IConventionEntityType> tenanted = entityTypes
            .Where(e => !e.IsOwned() && typeof(ITenanted).IsAssignableFrom(e.ClrType))
            .ToList();

        Dictionary<IConventionEntityType, IConventionKey> oldKeys = new();

        foreach (IConventionEntityType entityType in tenanted.Where(e => e.BaseType is null))
        {
            IConventionPropertyBuilder churchIdBuilder = entityType.Builder.Property(typeof(Guid), ChurchIdName)
                ?? throw Unprotected(entityType, $"the {ChurchIdName} property could not be configured");

            churchIdBuilder.IsRequired(true);
            IConventionProperty churchId = churchIdBuilder.Metadata;

            IConventionKey? primaryKey = entityType.FindPrimaryKey();
            IReadOnlyList<IConventionProperty> idProperties = primaryKey?.Properties
                ?? (entityType.FindProperty("Id") is { } id ? [id] : []);
            if (idProperties.Count == 0)
            {
                throw Unprotected(entityType, "it has no primary key and no Id property to combine with ChurchId");
            }

            if (!idProperties.Contains(churchId))
            {
                if (primaryKey is not null)
                {
                    oldKeys[entityType] = primaryKey;
                }

                _ = entityType.SetPrimaryKey([churchId, .. idProperties])
                    ?? throw Unprotected(entityType, "the (ChurchId, Id) primary key could not be configured");
            }

            entityType.SetQueryFilter(FilterName, BuildFilter(entityType.ClrType));
        }

        foreach (IConventionEntityType dependent in tenanted)
        {
            IConventionProperty churchId = dependent.FindProperty(ChurchIdName)
                ?? throw Unprotected(dependent, $"it has no {ChurchIdName} property");

            foreach (IConventionForeignKey fk in dependent.GetDeclaredForeignKeys().ToList())
            {
                IConventionEntityType principal = fk.PrincipalEntityType;
                if (fk.Properties.Contains(churchId) || !tenanted.Contains(principal))
                {
                    continue;
                }

                IConventionKey principalKey = principal.FindPrimaryKey()
                    ?? throw Unprotected(principal, "it has no primary key to reference from a tenanted dependent");
                if (!principalKey.Properties.Any(p => p.Name == ChurchIdName))
                {
                    throw Unprotected(principal, $"its primary key does not include {ChurchIdName}");
                }

                IConventionKey oldPrincipalKey = fk.PrincipalKey;
                IConventionKey newPrincipalKey = principalKey;
                if (oldPrincipalKey == newPrincipalKey)
                {
                    continue;
                }

                // Change both sides atomically, preserving explicitly configured navigations and delete behavior.
                fk.SetProperties([churchId, .. fk.Properties], newPrincipalKey);
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

    private static InvalidOperationException Unprotected(IConventionEntityType entityType, string reason) =>
        new($"Tenancy cannot be enforced for {entityType.DisplayName()}: {reason}.");

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
