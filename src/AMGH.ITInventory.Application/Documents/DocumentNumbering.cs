using System.Globalization;
using System.Text.RegularExpressions;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Security;

namespace AMGH.ITInventory.Application.Documents;

/// <summary>A kind of transaction that gets a document number (Release 2.13). Viewing and editing its ERP reference need the given permissions.</summary>
public sealed record DocumentType(string Code, string Name, string ViewPermission, string EditPermission);

public static class DocumentTypes
{
    public static readonly DocumentType Movement = new("MOV", "Asset movement", Permissions.Movements.View, Permissions.Movements.Approve);
    public static readonly DocumentType Maintenance = new("MNT", "Maintenance job", Permissions.Maintenance.View, Permissions.Maintenance.Manage);
    public static readonly DocumentType Disposal = new("DSP", "Disposal", Permissions.Disposals.View, Permissions.Disposals.Complete);
    public static readonly DocumentType Assignment = new("ASG", "Assignment", Permissions.Assignments.View, Permissions.Assignments.Assign);
    public static readonly DocumentType Verification = new("VER", "Physical verification", Permissions.Audits.View, Permissions.Audits.Perform);
    public static readonly DocumentType Warranty = new("WRN", "Warranty", Permissions.Warranties.View, Permissions.Warranties.Manage);

    public static readonly IReadOnlyList<DocumentType> All = new[] { Movement, Maintenance, Disposal, Assignment, Verification, Warranty };

    public static DocumentType? Find(string? code) => All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));
}

public static class NumberingModes
{
    public const string Continuous = "Continuous";
    public const string Yearly = "Yearly";
}

public static class YearFormats
{
    public const string Year = "Year";   // 2026
    public const string FiscalYear = "FY"; // FY26
}

/// <summary>A company's document numbering settings.</summary>
public sealed record NumberingSettings(string CompanyCode, string Mode, int FiscalYearStartMonth, string YearFormat, int Digits);

/// <summary>Pure formatting rules (unit-tested): AMGH-MOV-000123, AMGH-MOV-2026-000123, AMGH-MOV-FY27-000123.</summary>
public static partial class DocumentNumberFormat
{
    /// <summary>
    /// The fiscal year label of <paramref name="date"/>: "" in Continuous mode; otherwise the year the fiscal year ENDS in
    /// (calendar year when it starts in January; e.g. a July start makes Jul 2026 - Jun 2027 "2027" / "FY27", as in SAP).
    /// </summary>
    public static string Period(NumberingSettings s, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.Mode != NumberingModes.Yearly) return string.Empty;
        var start = Math.Clamp(s.FiscalYearStartMonth, 1, 12);
        var endYear = start == 1 || date.Month < start ? date.Year : date.Year + 1;
        return s.YearFormat == YearFormats.FiscalYear
            ? "FY" + (endYear % 100).ToString("00", CultureInfo.InvariantCulture)
            : endYear.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The number never truncates: past the digits it simply grows (AMGH-MOV-1000000).</summary>
    public static string Format(NumberingSettings s, string typeCode, string period, int number)
    {
        ArgumentNullException.ThrowIfNull(s);
        var digits = Math.Clamp(s.Digits, 5, 8);
        var n = number.ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(period) ? $"{s.CompanyCode}-{typeCode}-{n}" : $"{s.CompanyCode}-{typeCode}-{period}-{n}";
    }

    [GeneratedRegex(@"^[A-Z0-9]{2,10}-(MOV|MNT|DSP|ASG|VER|WRN)-(?:(?:\d{4}|FY\d{2})-)?\d{5,10}$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>The document type of a text that looks like a document number, else null.</summary>
    public static DocumentType? TypeOf(string? text)
    {
        var t = (text ?? string.Empty).Trim();
        var m = Pattern().Match(t);
        return m.Success ? DocumentTypes.Find(m.Groups[1].Value) : null;
    }
}

/// <summary>The next counter value of a document type and period for the current company (SQL: usp_DocumentNumber_Next).</summary>
public interface IDocumentCounter
{
    Task<int> NextAsync(string typeCode, string period, CancellationToken cancellationToken = default);
}

public interface IDocumentNumbers
{
    Task<NumberingSettings> SettingsAsync(CancellationToken cancellationToken = default);
    Task<string> NextAsync(DocumentType type, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Gives the new document <paramref name="id"/> of <paramref name="type"/> its number (call inside the creating transaction).</summary>
    Task<string> AssignAsync(DocumentType type, int id, CancellationToken cancellationToken = default);
}

internal sealed class DocumentNumbers : IDocumentNumbers
{
    private readonly IUnitOfWork _uow;
    private readonly IDocumentCounter _counter;
    private readonly ICompanyContext _company;
    private readonly IDateTimeProvider _clock;

    public DocumentNumbers(IUnitOfWork uow, IDocumentCounter counter, ICompanyContext company, IDateTimeProvider clock)
    {
        _uow = uow;
        _counter = counter;
        _company = company;
        _clock = clock;
    }

    public async Task<NumberingSettings> SettingsAsync(CancellationToken cancellationToken = default)
    {
        var id = _company.CompanyId ?? throw new InvalidOperationException("Document numbers need a company context.");
        var rows = await _uow.Repository<Company>().QueryAsync(q => q.Where(c => c.CompanyId == id)
            .Select(c => new NumberingSettings(c.CompanyCode, c.DocNumberingMode, c.FiscalYearStartMonth, c.DocYearFormat, c.DocNumberDigits)), cancellationToken);
        return rows.Single();
    }

    public async Task<string> NextAsync(DocumentType type, DateOnly date, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        var s = await SettingsAsync(cancellationToken);
        var period = DocumentNumberFormat.Period(s, date);
        var n = await _counter.NextAsync(type.Code, period, cancellationToken);
        return DocumentNumberFormat.Format(s, type.Code, period, n);
    }

    public async Task<string> AssignAsync(DocumentType type, int id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        var ct = cancellationToken;
        return type.Code switch
        {
            "MOV" => await NumberAsync(await One<AssetMovement>(x => x.AssetMovementId == id, ct), e => e.DocumentNumber, (e, n) => e.DocumentNumber = n, type, ct),
            "MNT" => await NumberAsync(await One<AssetMaintenance>(x => x.AssetMaintenanceId == id, ct), e => e.DocumentNumber, (e, n) => e.DocumentNumber = n, type, ct),
            "DSP" => await NumberAsync(await One<AssetDisposal>(x => x.AssetDisposalId == id, ct), e => e.DocumentNumber, (e, n) => e.DocumentNumber = n, type, ct),
            "ASG" => await NumberAsync(await One<AssetAssignment>(x => x.AssetAssignmentId == id, ct), e => e.DocumentNumber, (e, n) => e.DocumentNumber = n, type, ct),
            "VER" => await NumberAsync(await One<AssetAudit>(x => x.AssetAuditId == id, ct), e => e.DocumentNumber, (e, n) => e.DocumentNumber = n, type, ct),
            "WRN" => await NumberAsync(await One<Warranty>(x => x.WarrantyId == id, ct), e => e.DocumentNumber, (e, n) => e.DocumentNumber = n, type, ct),
            _ => throw new ArgumentException("Unknown document type.", nameof(type))
        };
    }

    /// <summary>
    /// A document keeps its number for life: a record that already has one (e.g. a verification re-scanned in the same cycle,
    /// which updates the existing record) is returned as is, without consuming a counter value.
    /// </summary>
    private async Task<string> NumberAsync<T>(T entity, Func<T, string?> get, Action<T, string> set, DocumentType type, CancellationToken ct)
    {
        if (get(entity) is { Length: > 0 } existing) return existing;
        var number = await NextAsync(type, _clock.UtcToday, ct);
        set(entity, number);
        await _uow.SaveChangesAsync(ct);
        return number;
    }

    private async Task<T> One<T>(System.Linq.Expressions.Expression<Func<T, bool>> where, CancellationToken ct) where T : class =>
        await _uow.Repository<T>().FirstOrDefaultAsync(where, ct) ?? throw new InvalidOperationException($"{typeof(T).Name} not found for numbering.");
}
