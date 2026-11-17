using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Sheaf.Data.Tenancy;

/// <summary>
/// Rejects bulk assignments to mapped church ownership, and bulk writes that ignore the tenancy filter on
/// tenanted entities, before query translation.
/// </summary>
public sealed class TenancyQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        if (queryExpression is MethodCallExpression { Method.Name: var operation } bulk
            && bulk.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
            && operation is nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)
                or nameof(EntityFrameworkQueryableExtensions.ExecuteDelete))
        {
            TenancyFilterBypassFinder finder = new();
            finder.Visit(queryExpression);

            // EF applies IgnoreQueryFilters to every entity root in the query, wherever the call appears.
            // Roots alone miss navigation-reached targets (e.g. SelectMany), so the written type also counts.
            bool writesTenanted = bulk.Method.IsGenericMethod
                && typeof(ITenanted).IsAssignableFrom(bulk.Method.GetGenericArguments()[0]);
            if (finder.IgnoresTenancyFilter && (finder.HasTenantedRoot || writesTenanted))
            {
                throw new InvalidOperationException(
                    $"{operation} cannot ignore the {MultiTenancyConvention.FilterName} query filter on tenanted "
                    + "entities; bulk writes must stay scoped to the current church.");
            }
        }

        return new OwnershipUpdateVisitor(eventData.Context?.Model).Visit(queryExpression);
    }

    private sealed class TenancyFilterBypassFinder : ExpressionVisitor
    {
        public bool IgnoresTenancyFilter { get; private set; }

        public bool HasTenantedRoot { get; private set; }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is EntityQueryRootExpression root && typeof(ITenanted).IsAssignableFrom(root.EntityType.ClrType))
            {
                HasTenantedRoot = true;
            }

            return base.VisitExtension(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && node.Method.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters))
            {
                // Filter keys that cannot be inspected are treated as ignoring tenancy.
                IgnoresTenancyFilter |= node.Arguments.Count == 1
                    || node.Arguments[1] is not ConstantExpression { Value: IEnumerable<string> keys }
                    || keys.Contains(MultiTenancyConvention.FilterName);
            }

            return base.VisitMethodCall(node);
        }
    }

    private sealed class OwnershipUpdateVisitor(IModel? model) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && node.Method.Name == nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate))
            {
                // EF 10 normalizes both public APIs, including runtime builder actions, into setter tuples.
                if (model is null || node.Arguments.Count != 2
                    || node.Arguments[1] is not NewArrayExpression setters)
                {
                    throw UnverifiableTarget();
                }

                foreach (Expression setter in setters.Expressions)
                {
                    if (setter is not NewExpression { Arguments.Count: 2 } tuple
                        || Unwrap(tuple.Arguments[0]) is not LambdaExpression { Parameters.Count: 1 } property)
                    {
                        throw UnverifiableTarget();
                    }

                    (IEntityType entityType, IProperty mappedProperty) =
                        ResolveProperty(model, node.Arguments[0], property.Parameters[0], property.Body);

                    if (mappedProperty.Name == nameof(ITenanted.ChurchId)
                        && typeof(ITenanted).IsAssignableFrom(entityType.ClrType))
                    {
                        throw new InvalidOperationException(
                            "ChurchId is immutable and cannot be assigned by ExecuteUpdate.");
                    }
                }
            }

            return base.VisitMethodCall(node);
        }
    }

    private static (IEntityType EntityType, IProperty Property) ResolveProperty(
        IModel model, Expression source, ParameterExpression parameter, Expression target)
    {
        if (source is EntityQueryRootExpression root)
        {
            if (!parameter.Type.IsAssignableFrom(root.EntityType.ClrType))
            {
                throw UnverifiableTarget();
            }

            if (parameter.Type != root.EntityType.ClrType)
            {
                target = ReplacingExpressionVisitor.Replace(parameter,
                    Expression.Convert(Expression.Parameter(root.EntityType.ClrType), parameter.Type), target);
            }

            if (TryPropertyAccess(target, out Expression instance, out string name)
                && model.FindEntityType(Unwrap(instance).Type) is { } entityType
                && entityType.FindProperty(name) is { } mappedProperty)
            {
                return (entityType, mappedProperty);
            }

            throw UnverifiableTarget();
        }

        if (source is not MethodCallExpression query)
        {
            throw UnverifiableTarget();
        }

        if (query.Method.DeclaringType == typeof(Queryable)
            && query.Method.Name is nameof(Queryable.OfType) or nameof(Queryable.Cast))
        {
            Type inputType = query.Arguments[0].Type;
            Type elementType = inputType.GetInterfaces().Append(inputType)
                .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>))
                .Select(type => type.GenericTypeArguments[0])
                .SingleOrDefault() ?? throw UnverifiableTarget();
            ParameterExpression input = Expression.Parameter(elementType);

            // Rebind the input type so intervening operators still resolve across casts.
            Expression inputTarget = ReplacingExpressionVisitor.Replace(parameter,
                Expression.Convert(input, parameter.Type), target);
            return ResolveProperty(model, query.Arguments[0], input, inputTarget);
        }

        if (query.Method.DeclaringType == typeof(Queryable)
            && query.Method.Name == nameof(Queryable.Select)
            && Unwrap(query.Arguments[1]) is LambdaExpression { Parameters.Count: 1 } projection)
        {
            Expression projectedTarget = ReplacingExpressionVisitor.Replace(parameter, projection.Body, target);
            return ResolveProperty(model, query.Arguments[0], projection.Parameters[0], projectedTarget);
        }

        if (query.Method.DeclaringType == typeof(Queryable)
            && query.Method.Name is nameof(Queryable.Union) or nameof(Queryable.Concat)
                or nameof(Queryable.Intersect) or nameof(Queryable.Except))
        {
            var left = ResolveProperty(model, query.Arguments[0], parameter, target);
            var right = ResolveProperty(model, query.Arguments[1], parameter, target);

            // Matching alias names alone do not prove that both branches update the same mapped column.
            return left == right ? left : throw UnverifiableTarget();
        }

        if (query.Method.DeclaringType == typeof(Queryable)
            && (((query.Method.Name is nameof(Queryable.Join) or nameof(Queryable.LeftJoin)
                    or nameof(Queryable.RightJoin)) && query.Arguments.Count == 5)
                || (query.Method.Name == nameof(Queryable.SelectMany) && query.Arguments.Count == 3))
            && Unwrap(query.Arguments[^1]) is LambdaExpression { Parameters.Count: 2 } result)
        {
            Expression joinedTarget = ReplacingExpressionVisitor.Replace(parameter, result.Body, target);
            Expression accessRoot = Unwrap(joinedTarget);
            while (TryPropertyAccess(accessRoot, out Expression instance, out _))
            {
                accessRoot = Unwrap(instance);
            }

            for (int index = 0; index < result.Parameters.Count; index++)
            {
                if (accessRoot == result.Parameters[index])
                {
                    Expression input = query.Arguments[index];
                    if (index == 1 && query.Method.Name == nameof(Queryable.SelectMany))
                    {
                        input = Unwrap(input) is LambdaExpression collection
                            ? collection.Body
                            : throw UnverifiableTarget();
                    }

                    return ResolveProperty(model, input, result.Parameters[index], joinedTarget);
                }
            }

            throw UnverifiableTarget();
        }

        if (query.Method.DeclaringType == typeof(Queryable)
            && query.Method.Name == nameof(Queryable.SelectMany)
            && query.Arguments.Count == 2
            && Unwrap(query.Arguments[1]) is LambdaExpression collectionSelector)
        {
            return ResolveProperty(model, collectionSelector.Body, parameter, target);
        }

        if (PreservesElement(query, parameter.Type))
        {
            return ResolveProperty(model, query.Arguments[0], parameter, target);
        }

        throw UnverifiableTarget();
    }

    private static bool PreservesElement(MethodCallExpression query, Type elementType)
    {
        if (query.Arguments.Count == 0
            || !typeof(IQueryable<>).MakeGenericType(elementType).IsAssignableFrom(query.Arguments[0].Type))
        {
            return false;
        }

        if (query.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
            || query.Method.DeclaringType == typeof(RelationalQueryableExtensions))
        {
            return true;
        }

        return query.Method.DeclaringType == typeof(Queryable)
            && query.Method.Name is nameof(Queryable.Where) or nameof(Queryable.OrderBy)
                or nameof(Queryable.OrderByDescending) or nameof(Queryable.ThenBy)
                or nameof(Queryable.ThenByDescending) or nameof(Queryable.Skip) or nameof(Queryable.Take)
                or nameof(Queryable.Distinct) or nameof(Queryable.Reverse) or nameof(Queryable.AsQueryable);
    }

    private static bool TryPropertyAccess(Expression expression, out Expression instance, out string name)
    {
        switch (Unwrap(expression))
        {
            case MemberExpression { Expression: { } owner } member:
                instance = owner;
                name = member.Member.Name;
                return true;

            case MethodCallExpression call when call.Method.DeclaringType == typeof(EF)
                && call.Method.Name == nameof(EF.Property) && call.Arguments.Count == 2
                && call.Arguments[1] is ConstantExpression { Value: string propertyName }:
                instance = call.Arguments[0];
                name = propertyName;
                return true;

            default:
                instance = expression;
                name = string.Empty;
                return false;
        }
    }

    private static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression
            { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs or ExpressionType.Quote } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static InvalidOperationException UnverifiableTarget() =>
        new("ExecuteUpdate cannot verify the mapped setter target; ChurchId ownership is immutable.");
}
