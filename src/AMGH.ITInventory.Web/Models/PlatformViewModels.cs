using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Security.Platform;

namespace AMGH.ITInventory.Web.Models;

public sealed class CompanyWizardViewModel
{
    public CompanySetupRequest Setup { get; init; } = new();
    public IReadOnlyList<LookupOption> Currencies { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<(string Id, string Name)> TimeZones { get; init; } = Array.Empty<(string, string)>();
    public IReadOnlyList<(int Id, string Name)> Companies { get; init; } = Array.Empty<(int, string)>();
}

public sealed class CompanyEditViewModel
{
    public int CompanyId { get; init; }
    public CompanyDetailsForm Details { get; init; } = new();
    public IReadOnlyList<LookupOption> Currencies { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<(string Id, string Name)> TimeZones { get; init; } = Array.Empty<(string, string)>();
    public IReadOnlyList<(int Id, string Name)> Companies { get; init; } = Array.Empty<(int, string)>();
}

/// <summary>Shared by the wizard and the edit page: renders the company detail and branding fields.</summary>
public sealed record CompanyFieldsModel(string Prefix, CompanyDetailsForm Details, bool IsNew,
    IReadOnlyList<LookupOption> Currencies, IReadOnlyList<(string Id, string Name)> TimeZones, string Section,
    IReadOnlyList<(int Id, string Name)>? Companies = null);
