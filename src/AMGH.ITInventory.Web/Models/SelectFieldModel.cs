using AMGH.ITInventory.Application.Common;

namespace AMGH.ITInventory.Web.Models;

/// <summary>A drop-down rendered by Views/Shared/_SelectField. <see cref="ParentSelectId"/> enables cascading.</summary>
public sealed record SelectFieldModel(
    string Name,
    string Label,
    IReadOnlyList<LookupOption> Options,
    string? Selected,
    bool Required = false,
    string? ParentSelectId = null,
    string? Help = null,
    bool Disabled = false,
    string EmptyText = "— Select —",
    string? CssClass = "col-md-6")
{
    public string Id => Name.Replace('.', '_');
}
