using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Application.Abstractions.Persistence;

public static class QueryableExtensions
{
    /// <summary>
    /// Excludes soft-deleted rows. Needed for master/lookup data (pickers, dropdowns), which the
    /// global query filter deliberately leaves unfiltered so historic references still resolve.
    /// </summary>
    public static IQueryable<T> NotDeleted<T>(this IQueryable<T> source) where T : ISoftDelete
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Where(e => !e.IsDeleted);
    }
}
