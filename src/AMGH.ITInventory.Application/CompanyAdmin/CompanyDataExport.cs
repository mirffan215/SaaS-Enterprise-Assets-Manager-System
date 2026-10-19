using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AMGH.ITInventory.Application.Abstractions.Mail;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.CompanyAdmin;

/// <summary>One file of an archive.</summary>
public sealed record ArchiveEntry(string Name, byte[] Content);

/// <summary>Writes a password-protected (AES-256) .zip archive. Implemented in Infrastructure.</summary>
public interface IEncryptedArchiveWriter
{
    void Write(Stream output, string password, IReadOnlyList<ArchiveEntry> entries);
}

public sealed record DataExportItem(int Id, string Status, string Reason, string RequestedBy, DateTime RequestedUtc, string? ApprovedBy,
    DateTime? ApprovedUtc, string? RejectionReason, DateTime? ExpiresUtc, DateTime? DownloadedUtc);

/// <summary>What the requester needs to download an approved export.</summary>
public sealed record DataExportCredentials(string Token, string Password, DateTime ExpiresUtc);

/// <summary>A finished archive on disk; disposing it deletes the temporary file.</summary>
public sealed class ExportArchive : IAsyncDisposable
{
    public ExportArchive(Stream content, string fileName)
    {
        Content = content;
        FileName = fileName;
    }

    public Stream Content { get; }
    public string FileName { get; }

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface ICompanyDataExportService
{
    Task<IReadOnlyList<DataExportItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The token and archive password of an approved, unexpired export; only for the person who requested it.</summary>
    Task<DataExportCredentials?> CredentialsAsync(int requestId, string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the one-time token, builds the encrypted archive, marks the export downloaded and records the download in the
    /// audit log. The token works once, only for the requester, and only while the export is approved and unexpired.
    /// </summary>
    Task<Result<ExportArchive>> DownloadAsync(int requestId, string token, CancellationToken cancellationToken = default);
}

/// <summary>Token and password handed out when a second administrator approves an export.</summary>
internal static class ExportCredentialFactory
{
    internal static readonly TimeSpan Validity = TimeSpan.FromHours(24);
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    internal static void Issue(DataExportRequest request, DateTime nowUtc, ISecretProtector protector)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var password = string.Create(24, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        });
        request.TokenProtected = protector.Protect(token);
        request.PasswordProtected = protector.Protect(password);
        request.TokenExpiresDate = nowUtc.Add(Validity);
    }
}

internal sealed class CompanyDataExportService : ICompanyDataExportService
{
    private readonly IUnitOfWork _uow;
    private readonly IEncryptedArchiveWriter _archive;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _user;

    public CompanyDataExportService(IUnitOfWork uow, IEncryptedArchiveWriter archive, ISecretProtector protector, IDateTimeProvider clock, ICurrentUserService user)
    {
        _uow = uow;
        _archive = archive;
        _protector = protector;
        _clock = clock;
        _user = user;
    }

    public async Task<IReadOnlyList<DataExportItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        // approved exports past their time limit become Expired the first time somebody looks
        var lapsed = await _uow.Repository<DataExportRequest>().ListAsync(r => r.Status == DataExportStatuses.Approved && r.TokenExpiresDate < now, cancellationToken);
        foreach (var r in lapsed) { r.Status = DataExportStatuses.Expired; r.TokenProtected = null; r.PasswordProtected = null; }
        if (lapsed.Count > 0) await _uow.SaveChangesAsync(cancellationToken);

        return await _uow.Repository<DataExportRequest>().QueryAsync(q => q.OrderByDescending(r => r.DataExportRequestId).Take(100)
            .Select(r => new DataExportItem(r.DataExportRequestId, r.Status, r.Reason, r.RequestedBy, r.RequestedDate, r.ApprovedBy, r.ApprovedDate,
                r.RejectionReason, r.TokenExpiresDate, r.DownloadedDate)), cancellationToken);
    }

    public async Task<DataExportCredentials?> CredentialsAsync(int requestId, string userName, CancellationToken cancellationToken = default)
    {
        var r = await _uow.Repository<DataExportRequest>().FirstOrDefaultAsync(x => x.DataExportRequestId == requestId, cancellationToken);
        if (r is null || r.Status != DataExportStatuses.Approved || !string.Equals(r.RequestedBy, userName, StringComparison.OrdinalIgnoreCase)) return null;
        if (r.TokenExpiresDate is not { } expires || expires < _clock.UtcNow) return null;
        var token = r.TokenProtected is null ? null : _protector.Unprotect(r.TokenProtected);
        var password = r.PasswordProtected is null ? null : _protector.Unprotect(r.PasswordProtected);
        return token is null || password is null ? null : new DataExportCredentials(token, password, expires);
    }

    public async Task<Result<ExportArchive>> DownloadAsync(int requestId, string token, CancellationToken cancellationToken = default)
    {
        var r = await _uow.Repository<DataExportRequest>().FirstOrDefaultAsync(x => x.DataExportRequestId == requestId, cancellationToken);
        if (r is null || r.Status != DataExportStatuses.Approved)
            return Result<ExportArchive>.Failure(Error.NotFound("This export is not available for download (not approved, already downloaded, or expired)."));
        if (!string.Equals(r.RequestedBy, _user.UserName, StringComparison.OrdinalIgnoreCase))
            return Result<ExportArchive>.Failure(Error.Forbidden("Only the person who requested the export can download it."));
        if (r.TokenExpiresDate is not { } expires || expires < _clock.UtcNow)
            return Result<ExportArchive>.Failure(Error.BusinessRule("The download time limit has passed. Request the export again."));
        var expected = r.TokenProtected is null ? null : _protector.Unprotect(r.TokenProtected);
        var password = r.PasswordProtected is null ? null : _protector.Unprotect(r.PasswordProtected);
        if (expected is null || password is null || string.IsNullOrEmpty(token)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token)))
            return Result<ExportArchive>.Failure(Error.Forbidden("The download token is not valid."));

        var (entries, counts) = await BuildEntriesAsync(cancellationToken);
        var path = Path.Combine(Path.GetTempPath(), $"eams-export-{Guid.NewGuid():N}.zip");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            _archive.Write(file, password, entries);
            file.Position = 0;

            // one-time: mark it downloaded (a parallel second download loses the concurrency check) and record it in the audit log
            r.Status = DataExportStatuses.Downloaded;
            r.DownloadedBy = _user.UserName;
            r.DownloadedDate = _clock.UtcNow;
            r.TokenProtected = null;
            r.PasswordProtected = null;
            await _uow.Repository<AuditLog>().AddAsync(new AuditLog
            {
                EntityName = "CompanyDataExport", EntityKey = r.DataExportRequestId.ToString(CultureInfo.InvariantCulture), Action = Domain.Enums.AuditAction.Export,
                NewValues = JsonSerializer.Serialize(new { files = counts, approvedBy = r.ApprovedBy }), PerformedBy = _user.UserName,
                IpAddress = _user.IpAddress, CorrelationId = _user.CorrelationId, EventDate = _clock.UtcNow
            }, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
        return Result<ExportArchive>.Success(new ExportArchive(file, $"company-data-{_clock.UtcNow:yyyyMMdd-HHmm}.zip"));
    }

    // ---------------------------------------------------------------- content: one CSV per table (no password hashes, no license keys)
    private async Task<(IReadOnlyList<ArchiveEntry> Entries, Dictionary<string, int> Counts)> BuildEntriesAsync(CancellationToken ct)
    {
        var entries = new List<ArchiveEntry>();
        var counts = new Dictionary<string, int>();
        async Task Add<TEntity, TRow>(string name, Func<IQueryable<TEntity>, IQueryable<TRow>> shape) where TEntity : class where TRow : notnull
        {
            var rows = await _uow.Repository<TEntity>().QueryAsync(shape, ct);
            counts[name] = rows.Count;
            entries.Add(new ArchiveEntry(name + ".csv", Csv.Write(rows)));
        }

        await Add<Asset, object>("assets", q => q.OrderBy(a => a.AssetId).Select(a => (object)new
        {
            a.AssetNumber, a.AssetName, a.AssetCategoryId, a.AssetSubCategoryId, a.AssetStatusId, a.ManufacturerId, a.ModelName, a.SerialNumber, a.Hostname,
            a.IpAddress, a.MacAddress, a.OperatingSystem, a.VendorId, a.PurchaseOrderNumber, a.InvoiceNumber, a.PurchaseDate, a.PurchaseCost, a.CurrencyId,
            a.AssignedEmployeeId, a.DepartmentId, a.LocationId, a.BuildingId, a.FloorId, a.RoomId, a.CreatedDate
        }));
        await Add<AssetAssignment, object>("assignments", q => q.OrderBy(a => a.AssetAssignmentId).Select(a => (object)new
        {
            a.AssetId, a.EmployeeId, a.DepartmentId, a.LocationId, a.AssignmentType, a.AssignedDate, a.ReturnedDate, a.IsCurrent, a.AssignedBy, a.ReturnedTo
        }));
        await Add<Warranty, object>("warranties", q => q.OrderBy(w => w.WarrantyId).Select(w => (object)new
        {
            w.AssetId, w.WarrantyStatusId, w.WarrantyType, w.ContractNumber, w.StartDate, w.EndDate, w.SupportLevel, w.Cost, w.CurrencyId
        }));
        await Add<AssetMaintenance, object>("maintenance", q => q.OrderBy(m => m.AssetMaintenanceId).Select(m => (object)new
        {
            m.AssetId, m.MaintenanceType, m.MaintenanceStatus, m.ScheduledDate, m.CompletedDate, m.TicketNumber, m.ProblemDescription, m.ResolutionDetails,
            m.PerformedBy, m.Cost, m.DowntimeHours
        }));
        await Add<AssetMovement, object>("movements", q => q.OrderBy(m => m.AssetMovementId).Select(m => (object)new
        {
            m.AssetId, m.FromLocationId, m.ToLocationId, m.MovementType, m.MovementStatus, m.MovementReason, m.RequestedBy, m.RequestedDate, m.ApprovedBy, m.CompletedDate
        }));
        await Add<AssetDisposal, object>("disposals", q => q.OrderBy(d => d.AssetDisposalId).Select(d => (object)new
        {
            d.AssetId, d.DisposalMethodId, d.DisposalStatus, d.Reason, d.RequestedBy, d.RequestedDate, d.ApprovedBy, d.DisposalDate, d.RecipientName
        }));
        await Add<Employee, object>("employees", q => q.OrderBy(e => e.EmployeeId).Select(e => (object)new
        {
            e.EmployeeNumber, e.FirstName, e.LastName, e.Email, e.Phone, e.JobTitle, e.DepartmentId, e.LocationId, e.HireDate, e.TerminationDate, e.EmploymentStatus
        }));
        await Add<Department, object>("departments", q => q.OrderBy(d => d.DepartmentId).Select(d => (object)new { d.DepartmentId, d.DepartmentCode, d.DepartmentName, d.CostCenter, d.IsActive }));
        await Add<Location, object>("locations", q => q.OrderBy(l => l.LocationId).Select(l => (object)new { l.LocationId, l.LocationCode, l.LocationName, l.City, l.Country, l.IsActive }));
        await Add<Building, object>("buildings", q => q.OrderBy(b => b.BuildingId).Select(b => (object)new { b.BuildingId, b.LocationId, b.BuildingCode, b.BuildingName, b.IsActive }));
        await Add<Floor, object>("floors", q => q.OrderBy(f => f.FloorId).Select(f => (object)new { f.FloorId, f.BuildingId, f.FloorCode, f.FloorName, f.FloorNumber, f.IsActive }));
        await Add<Room, object>("rooms", q => q.OrderBy(r => r.RoomId).Select(r => (object)new { r.RoomId, r.FloorId, r.RoomCode, r.RoomName, r.IsActive }));
        await Add<Vendor, object>("vendors", q => q.OrderBy(v => v.VendorId).Select(v => (object)new { v.VendorId, v.VendorCode, v.VendorName, v.ContactPerson, v.Email, v.Phone, v.IsActive }));
        await Add<Manufacturer, object>("manufacturers", q => q.OrderBy(m => m.ManufacturerId).Select(m => (object)new { m.ManufacturerId, m.ManufacturerCode, m.ManufacturerName, m.Website, m.IsActive }));
        await Add<AssetCategory, object>("categories", q => q.OrderBy(c => c.AssetCategoryId).Select(c => (object)new { c.AssetCategoryId, c.CategoryCode, c.CategoryName }));
        await Add<AssetStatus, object>("statuses", q => q.OrderBy(s => s.AssetStatusId).Select(s => (object)new { s.AssetStatusId, s.StatusCode, s.StatusName }));
        entries.Add(new ArchiveEntry("README.txt", Encoding.UTF8.GetBytes(
            $"Company data export, created {_clock.UtcNow:yyyy-MM-dd HH:mm} UTC by {_user.UserName}.\r\n" +
            "One CSV file per table (UTF-8). Related rows are linked by the numeric ids. Password hashes and software license keys are not included.\r\n")));
        return (entries, counts);
    }
}

/// <summary>Minimal CSV writer. Text that a spreadsheet would run as a formula is prefixed with an apostrophe.</summary>
internal static class Csv
{
    private static readonly System.Buffers.SearchValues<char> NeedsQuotes = System.Buffers.SearchValues.Create(",\"\r\n");

    internal static byte[] Write<T>(IReadOnlyList<T> rows) where T : notnull
    {
        var sb = new StringBuilder();
        sb.Append('﻿');
        if (rows.Count == 0) return Encoding.UTF8.GetBytes(sb.ToString());
        var properties = rows[0].GetType().GetProperties();
        sb.AppendJoin(',', properties.Select(p => p.Name)).Append("\r\n");
        foreach (var row in rows)
        {
            for (var i = 0; i < properties.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Format(properties[i].GetValue(row)));
            }
            sb.Append("\r\n");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    internal static string Format(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        if (value is string && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        return text.AsSpan().IndexOfAny(NeedsQuotes) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }
}
