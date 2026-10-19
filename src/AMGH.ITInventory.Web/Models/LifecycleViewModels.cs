using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Application.Lifecycle;
using AMGH.ITInventory.Domain.Enums;

namespace AMGH.ITInventory.Web.Models;

public sealed class ApprovalRulesViewModel
{
    public IReadOnlyList<ApprovalRuleItem> Rules { get; init; } = Array.Empty<ApprovalRuleItem>();
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}

public sealed class MoveFormViewModel
{
    public required AssetHeader Asset { get; init; }
    public MovementForm Form { get; init; } = new();
    public IReadOnlyList<LookupOption> Locations { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Buildings { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Floors { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Rooms { get; init; } = Array.Empty<LookupOption>();
}

public sealed class MaintenanceListViewModel
{
    public IReadOnlyList<MaintenanceItem> Jobs { get; init; } = Array.Empty<MaintenanceItem>();
    public IReadOnlyList<LookupOption> Currencies { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<TechnicianOption> Technicians { get; init; } = Array.Empty<TechnicianOption>();
    public bool Mine { get; init; }
    public int? CurrentUserId { get; init; }
}

public sealed class MaintenanceFormViewModel
{
    public required AssetHeader Asset { get; init; }
    public MaintenanceForm Form { get; init; } = new();
    public IReadOnlyList<LookupOption> Vendors { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<TechnicianOption> Technicians { get; init; } = Array.Empty<TechnicianOption>();
}

public sealed class VerificationViewModel
{
    public string Cycle { get; init; } = string.Empty;
    public string? Scan { get; init; }
    public VerificationTarget? Target { get; init; }
    public bool NotFound { get; init; }
    public ScanMethod ScanMethod { get; init; }
    public required VerificationProgress Progress { get; init; }
    public IReadOnlyList<LookupOption> Locations { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Rooms { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Employees { get; init; } = Array.Empty<LookupOption>();
    public bool CanPerform { get; init; }
}

public sealed class DisposeFormViewModel
{
    public required AssetHeader Asset { get; init; }
    public int? MethodId { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<LookupOption> Methods { get; init; } = Array.Empty<LookupOption>();
}

public sealed class DisposalCompleteViewModel
{
    public required DisposalItem Disposal { get; init; }
    public DisposalCompletionForm Form { get; init; } = new();
    public IReadOnlyList<LookupOption> Currencies { get; init; } = Array.Empty<LookupOption>();
}

public sealed class WarrantyFormViewModel
{
    public required AssetHeader Asset { get; init; }
    public int? WarrantyId { get; init; }
    public WarrantyForm Form { get; init; } = new();
    public IReadOnlyList<LookupOption> Vendors { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Currencies { get; init; } = Array.Empty<LookupOption>();
}

public sealed class LicenseDetailsViewModel
{
    public required LicenseItem License { get; init; }
    public IReadOnlyList<LicenseAllocationItem> Allocations { get; init; } = Array.Empty<LicenseAllocationItem>();
    public IReadOnlyList<LookupOption> Employees { get; init; } = Array.Empty<LookupOption>();
}

public sealed class LicenseFormViewModel
{
    public int? LicenseId { get; init; }
    public LicenseForm Form { get; init; } = new();
    public IReadOnlyList<LookupOption> Publishers { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Vendors { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Currencies { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Employees { get; init; } = Array.Empty<LookupOption>();
}

public sealed class LicenseKeyViewModel
{
    public required LicenseItem License { get; init; }
    public string Key { get; init; } = string.Empty;
}

public sealed class RoleEditViewModel
{
    public int? RoleId { get; init; }
    public bool IsSystemRole { get; init; }
    public int Members { get; init; }
    public AMGH.ITInventory.Security.Accounts.RoleForm Form { get; init; } = new();
    public IReadOnlyList<AMGH.ITInventory.Security.Accounts.PermissionItem> Catalog { get; init; } = Array.Empty<AMGH.ITInventory.Security.Accounts.PermissionItem>();
}


public sealed class MasterImportViewModel
{
    public AMGH.ITInventory.Application.MasterData.MasterImportResult? Result { get; init; }
    public string? FileName { get; init; }
    public string? Message { get; init; }
}
