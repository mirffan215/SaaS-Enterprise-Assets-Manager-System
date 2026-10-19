using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Lifecycle;
using AMGH.ITInventory.Domain.Entities;

namespace AMGH.ITInventory.Application.Search;

// The ToLower() calls below run inside database queries, where EF Core translates them to SQL LOWER(): .NET culture
// plays no part there. (ToLowerInvariant() is not reliably translatable, so it cannot be used in these queries.)
// CA1862 suggests Contains(x, StringComparison.OrdinalIgnoreCase), which EF Core 8 cannot translate to SQL.
#pragma warning disable CA1304, CA1311, CA1862

public enum SearchKind { Asset, Employee, Licence, Department, Location, Vendor, Document }

public sealed record SearchHit(SearchKind Kind, int Id, string Title, string? Subtitle)
{
    /// <summary>For documents: the document type code (MOV, MNT, ...). An init property, so it can be set inside database queries.</summary>
    public string? DocType { get; init; }
}

public sealed record SearchGroup(SearchKind Kind, string Title, IReadOnlyList<SearchHit> Hits, bool More);

/// <summary><see cref="ExactAssetId"/> is set when the text is exactly one asset number (or a scanned label): open it directly.</summary>
public sealed record SearchResults(string Term, IReadOnlyList<SearchGroup> Groups, int? ExactAssetId, Documents.DocumentRef? ExactDocument = null)
{
    public int Count => Groups.Sum(g => g.Hits.Count);
}

/// <summary>What the user may see; groups they may not open are not searched at all. Permissions: document types are searched only where the user may view them.</summary>
public sealed record SearchScope(bool Assets, bool MasterData, bool Licences, IReadOnlySet<string>? Permissions = null);

public interface ISearchService
{
    const int MinLength = 2;
    Task<SearchResults> SearchAsync(string term, SearchScope scope, int perGroup = 8, CancellationToken cancellationToken = default);
}

/// <summary>Global search across the current company (company isolation applies to every query).</summary>
internal sealed class SearchService : ISearchService
{
    private readonly IUnitOfWork _uow;
    private readonly Documents.IDocumentService _documents;

    public SearchService(IUnitOfWork uow, Documents.IDocumentService documents)
    {
        _uow = uow;
        _documents = documents;
    }

    public async Task<SearchResults> SearchAsync(string term, SearchScope scope, int perGroup = 8, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var ct = cancellationToken;
        term = (term ?? string.Empty).Trim();
        if (term.Length > 100) term = term[..100];
        var groups = new List<SearchGroup>();
        if (term.Length < ISearchService.MinLength) return new SearchResults(term, groups, null);
        var take = Math.Clamp(perGroup, 1, 50) + 1;
        // compared in lower case on both sides: the same result on any SQL Server collation (and LIKE '%x%' uses no index anyway)
        var t = term.ToLowerInvariant();
        int? exact = null;

        if (scope.Assets)
        {
            // a scanned label or a typed asset number opens the asset directly
            if (ScanText.AssetNumber(term) is { } number)
            {
                var ids = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetNumber == number).Select(a => a.AssetId), ct);
                if (ids.Count == 1) exact = ids[0];
            }
            var hits = await _uow.Repository<Asset>().QueryAsync(q => q
                .Where(a => a.AssetNumber.ToLower().Contains(t) || a.AssetName.ToLower().Contains(t) || (a.SerialNumber != null && a.SerialNumber.ToLower().Contains(t))
                         || (a.Hostname != null && a.Hostname.ToLower().Contains(t)) || (a.MacAddress != null && a.MacAddress.ToLower().Contains(t))
                         || (a.IpAddress != null && a.IpAddress.ToLower().Contains(t))
                         || (a.PurchaseOrderNumber != null && a.PurchaseOrderNumber.ToLower().Contains(t))
                         || (a.InvoiceNumber != null && a.InvoiceNumber.ToLower().Contains(t))
                         || (a.AssignedEmployee != null && (a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName).ToLower().Contains(t)))
                .OrderBy(a => a.AssetNumber).Take(take)
                .Select(a => new SearchHit(SearchKind.Asset, a.AssetId, a.AssetNumber + " · " + a.AssetName,
                    a.AssetStatus.StatusName + (a.SerialNumber == null ? "" : " · S/N " + a.SerialNumber)
                    + (a.AssignedEmployee == null ? "" : " · " + a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName))), ct);
            Add(groups, SearchKind.Asset, "Assets", hits, perGroup);
        }
        if (scope.MasterData)
        {
            Add(groups, SearchKind.Employee, "Employees", await _uow.Repository<Employee>().QueryAsync(q => q
                .Where(e => (e.FirstName + " " + e.LastName).ToLower().Contains(t) || e.EmployeeNumber.ToLower().Contains(t) || (e.Email != null && e.Email.ToLower().Contains(t)))
                .OrderBy(e => e.FirstName).Take(take)
                .Select(e => new SearchHit(SearchKind.Employee, e.EmployeeId, e.FirstName + " " + e.LastName + " (" + e.EmployeeNumber + ")",
                    (e.JobTitle ?? "") + (e.Department == null ? "" : " · " + e.Department.DepartmentName))), ct), perGroup);
            Add(groups, SearchKind.Department, "Departments", await _uow.Repository<Department>().QueryAsync(q => q
                .Where(d => d.DepartmentName.ToLower().Contains(t) || d.DepartmentCode.ToLower().Contains(t)).OrderBy(d => d.DepartmentName).Take(take)
                .Select(d => new SearchHit(SearchKind.Department, d.DepartmentId, d.DepartmentName, d.DepartmentCode)), ct), perGroup);
            Add(groups, SearchKind.Location, "Locations", await _uow.Repository<Location>().QueryAsync(q => q
                .Where(l => l.LocationName.ToLower().Contains(t) || l.LocationCode.ToLower().Contains(t) || (l.City != null && l.City.ToLower().Contains(t)))
                .OrderBy(l => l.LocationName).Take(take)
                .Select(l => new SearchHit(SearchKind.Location, l.LocationId, l.LocationName, l.City)), ct), perGroup);
            Add(groups, SearchKind.Vendor, "Vendors", await _uow.Repository<Vendor>().QueryAsync(q => q
                .Where(v => v.VendorName.ToLower().Contains(t) || v.VendorCode.ToLower().Contains(t)).OrderBy(v => v.VendorName).Take(take)
                .Select(v => new SearchHit(SearchKind.Vendor, v.VendorId, v.VendorName, v.VendorCode)), ct), perGroup);
        }
        if (scope.Licences)
        {
            Add(groups, SearchKind.Licence, "Software licences", await _uow.Repository<SoftwareLicense>().QueryAsync(q => q
                .Where(l => l.SoftwareName.ToLower().Contains(t) || l.LicenseNumber.ToLower().Contains(t)
                         || (l.PurchaseOrderNumber != null && l.PurchaseOrderNumber.ToLower().Contains(t))
                         || (l.InvoiceNumber != null && l.InvoiceNumber.ToLower().Contains(t))).OrderBy(l => l.SoftwareName).Take(take)
                .Select(l => new SearchHit(SearchKind.Licence, l.SoftwareLicenseId, l.SoftwareName + (l.SoftwareVersion == null ? "" : " " + l.SoftwareVersion), l.LicenseNumber)), ct), perGroup);
        }
        // ---- documents (Release 2.13): only the types the user may view are searched at all
        Documents.DocumentRef? exactDoc = null;
        if (scope.Permissions is { } perms)
        {
            exactDoc = await _documents.FindAsync(term, perms, ct);
            bool May(Documents.DocumentType d) => perms.Contains(d.ViewPermission);
            const string Sep = " \u00b7 ";
            if (May(Documents.DocumentTypes.Movement))
                Add(groups, SearchKind.Document, "Asset movements", await _uow.Repository<AssetMovement>().QueryAsync(q => q
                    .Where(x => (x.DocumentNumber != null && x.DocumentNumber.ToLower().Contains(t)) || (x.ErpReference != null && x.ErpReference.ToLower().Contains(t))
                             || (x.ChangeRequestNumber != null && x.ChangeRequestNumber.ToLower().Contains(t)))
                    .OrderByDescending(x => x.AssetMovementId).Take(take)
                    .Select(x => new SearchHit(SearchKind.Document, x.AssetMovementId, (x.DocumentNumber ?? "(no number)") + Sep + "Movement",
                        x.Asset.AssetNumber + Sep + x.MovementStatus.ToString() + (x.ChangeRequestNumber == null ? "" : Sep + "CR " + x.ChangeRequestNumber)
                        + (x.ErpReference == null ? "" : Sep + "ERP " + x.ErpReference)) { DocType = "MOV" }), ct), perGroup);
            if (May(Documents.DocumentTypes.Maintenance))
                Add(groups, SearchKind.Document, "Maintenance jobs", await _uow.Repository<AssetMaintenance>().QueryAsync(q => q
                    .Where(x => (x.DocumentNumber != null && x.DocumentNumber.ToLower().Contains(t)) || (x.ErpReference != null && x.ErpReference.ToLower().Contains(t))
                             || (x.TicketNumber != null && x.TicketNumber.ToLower().Contains(t)))
                    .OrderByDescending(x => x.AssetMaintenanceId).Take(take)
                    .Select(x => new SearchHit(SearchKind.Document, x.AssetMaintenanceId, (x.DocumentNumber ?? "(no number)") + Sep + "Maintenance",
                        x.Asset.AssetNumber + Sep + x.MaintenanceStatus.ToString() + (x.TicketNumber == null ? "" : Sep + "Ticket " + x.TicketNumber)
                        + (x.ErpReference == null ? "" : Sep + "ERP " + x.ErpReference)) { DocType = "MNT" }), ct), perGroup);
            if (May(Documents.DocumentTypes.Disposal))
                Add(groups, SearchKind.Document, "Disposals", await _uow.Repository<AssetDisposal>().QueryAsync(q => q
                    .Where(x => (x.DocumentNumber != null && x.DocumentNumber.ToLower().Contains(t)) || (x.ErpReference != null && x.ErpReference.ToLower().Contains(t))
                             || (x.CertificateNumber != null && x.CertificateNumber.ToLower().Contains(t)))
                    .OrderByDescending(x => x.AssetDisposalId).Take(take)
                    .Select(x => new SearchHit(SearchKind.Document, x.AssetDisposalId, (x.DocumentNumber ?? "(no number)") + Sep + "Disposal",
                        x.Asset.AssetNumber + Sep + x.DisposalStatus.ToString() + (x.CertificateNumber == null ? "" : Sep + "Certificate " + x.CertificateNumber)
                        + (x.ErpReference == null ? "" : Sep + "ERP " + x.ErpReference)) { DocType = "DSP" }), ct), perGroup);
            if (May(Documents.DocumentTypes.Assignment))
                Add(groups, SearchKind.Document, "Assignments", await _uow.Repository<AssetAssignment>().QueryAsync(q => q
                    .Where(x => (x.DocumentNumber != null && x.DocumentNumber.ToLower().Contains(t)) || (x.ErpReference != null && x.ErpReference.ToLower().Contains(t)))
                    .OrderByDescending(x => x.AssetAssignmentId).Take(take)
                    .Select(x => new SearchHit(SearchKind.Document, x.AssetAssignmentId, (x.DocumentNumber ?? "(no number)") + Sep + "Assignment",
                        x.Asset.AssetNumber + Sep + (x.IsCurrent ? "Current" : "Returned") + (x.ErpReference == null ? "" : Sep + "ERP " + x.ErpReference)) { DocType = "ASG" }), ct), perGroup);
            if (May(Documents.DocumentTypes.Verification))
                Add(groups, SearchKind.Document, "Verifications", await _uow.Repository<AssetAudit>().QueryAsync(q => q
                    .Where(x => (x.DocumentNumber != null && x.DocumentNumber.ToLower().Contains(t)) || (x.ErpReference != null && x.ErpReference.ToLower().Contains(t)))
                    .OrderByDescending(x => x.AssetAuditId).Take(take)
                    .Select(x => new SearchHit(SearchKind.Document, x.AssetAuditId, (x.DocumentNumber ?? "(no number)") + Sep + "Verification",
                        x.Asset.AssetNumber + Sep + x.AuditCycle + Sep + x.AuditResult.ToString()) { DocType = "VER" }), ct), perGroup);
            if (May(Documents.DocumentTypes.Warranty))
                Add(groups, SearchKind.Document, "Warranties", await _uow.Repository<Warranty>().QueryAsync(q => q
                    .Where(x => (x.DocumentNumber != null && x.DocumentNumber.ToLower().Contains(t)) || (x.ErpReference != null && x.ErpReference.ToLower().Contains(t))
                             || (x.ContractNumber != null && x.ContractNumber.ToLower().Contains(t)))
                    .OrderByDescending(x => x.WarrantyId).Take(take)
                    .Select(x => new SearchHit(SearchKind.Document, x.WarrantyId, (x.DocumentNumber ?? "(no number)") + Sep + "Warranty",
                        x.Asset.AssetNumber + (x.ContractNumber == null ? "" : Sep + "Contract " + x.ContractNumber)
                        + (x.ErpReference == null ? "" : Sep + "ERP " + x.ErpReference)) { DocType = "WRN" }), ct), perGroup);
        }
        return new SearchResults(term, groups, exact, exactDoc);
    }

    private static void Add(List<SearchGroup> groups, SearchKind kind, string title, List<SearchHit> hits, int perGroup)
    {
        if (hits.Count == 0) return;
        groups.Add(new SearchGroup(kind, title, hits.Take(perGroup).ToList(), hits.Count > perGroup));
    }
}
