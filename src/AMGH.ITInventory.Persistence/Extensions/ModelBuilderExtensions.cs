using System.Linq.Expressions;
using AMGH.ITInventory.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Extensions;

internal static class ModelBuilderExtensions
{
    /// <summary>Maps the standard audit columns present on every business table.</summary>
    public static void ConfigureAuditColumns<T>(this EntityTypeBuilder<T> builder) where T : AuditableEntity
    {
        builder.Property(e => e.CreatedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.CreatedDate).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.ModifiedBy).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.ModifiedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.DeletedBy).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.DeletedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.IsDeleted).HasColumnType("bit").IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();
    }

    /// <summary>
    /// One query filter per entity combining:
    /// <list type="bullet">
    /// <item>company isolation, <c>CompanyId = current company</c>, on every company-owned entity (lookups included);</item>
    /// <item>soft delete, <c>IsDeleted = 0</c>, except the lookup types in <paramref name="softDeleteExcluded"/>.</item>
    /// </list>
    /// The company value is read from <paramref name="context"/> per query (EF parameterises DbContext members).
    /// </summary>
    public static void ApplyQueryFilters(this ModelBuilder modelBuilder, IReadOnlySet<Type> softDeleteExcluded, AMGHDbContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (entityType.BaseType is not null) continue;

            var parameter = Expression.Parameter(clr, "e");
            Expression? body = CompanyPredicate(clr, parameter, context);

            if (typeof(ISoftDelete).IsAssignableFrom(clr) && !softDeleteExcluded.Contains(clr))
            {
                Expression notDeleted = Expression.Equal(
                    Expression.Property(parameter, nameof(ISoftDelete.IsDeleted)), Expression.Constant(false));
                body = body is null ? notDeleted : Expression.AndAlso(body, notDeleted);
            }

            if (body is not null) modelBuilder.Entity(clr).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    /// <summary><c>e.CompanyId == context.CurrentCompanyId</c> for company-owned and company-scoped types; null otherwise.</summary>
    internal static Expression? CompanyPredicate(Type clr, ParameterExpression parameter, AMGHDbContext context)
    {
        var current = Expression.Property(Expression.Constant(context), nameof(AMGHDbContext.CurrentCompanyId));
        if (typeof(ICompanyOwned).IsAssignableFrom(clr))
            return Expression.Equal(Expression.Property(parameter, nameof(ICompanyOwned.CompanyId)), current);
        if (typeof(ICompanyScoped).IsAssignableFrom(clr))
            return Expression.Equal(Expression.Property(parameter, nameof(ICompanyScoped.CompanyId)),
                Expression.Convert(current, typeof(int?)));
        return null;
    }
}
