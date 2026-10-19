using AMGH.ITInventory.Shared.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AMGH.ITInventory.Web.Infrastructure;

public static class ModelStateExtensions
{
    /// <summary>
    /// Adds service errors to ModelState. Error codes that name a form field (e.g. "Email")
    /// attach to that field; others become page-level errors.
    /// </summary>
    public static void AddErrors(this ModelStateDictionary modelState, IEnumerable<Error> errors,
        IReadOnlyDictionary<string, string>? fieldMap = null)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var error in errors)
        {
            var key = fieldMap is not null && fieldMap.TryGetValue(error.Code, out var mapped)
                ? mapped
                : error.Code.Length > 0 && char.IsUpper(error.Code[0]) ? error.Code : string.Empty;
            modelState.AddModelError(key, error.Message);
        }
    }
}
