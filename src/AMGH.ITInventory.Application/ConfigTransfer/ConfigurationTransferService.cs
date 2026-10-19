using System.Globalization;
using System.Text.Json;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Extensions.Caching.Memory;

namespace AMGH.ITInventory.Application.ConfigTransfer;

/// <summary>
/// Moves a company's configuration (asset categories, classifications, document numbering, locations) as XML or directly
/// to another company of the same tenant database. Every import is a preview first: the incoming settings are parsed and
/// validated once, kept on the server under a one-time token, and only the rows the user ticks are saved, in one transaction.
/// </summary>
public interface IConfigurationTransferService
{
    /// <summary>The current company's configuration as XML.</summary>
    Task<byte[]> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>Parses and validates an uploaded file and shows what importing it would add or change.</summary>
    Task<Result<ConfigPreview>> PreviewImportAsync(Stream xml, CancellationToken cancellationToken = default);

    /// <summary>
    /// Preview of copying <paramref name="sourceCompanyId"/>'s configuration into the current company. The caller must have
    /// checked that the signed-in user may access the source company.
    /// </summary>
    Task<Result<ConfigPreview>> PreviewCopyAsync(int sourceCompanyId, CancellationToken cancellationToken = default);

    /// <summary>Saves the ticked rows of a preview. The token works once, for the same user and company only.</summary>
    Task<Result<ConfigImportResult>> CommitAsync(string token, IReadOnlyCollection<string> selectedKeys, CancellationToken cancellationToken = default);
}

internal sealed class ConfigurationTransferService : IConfigurationTransferService
{
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(20);

    private sealed record Pending(string UserName, int CompanyId, ConfigurationDocument Document, IReadOnlyList<ConfigChange> Changes, string Source);

    private readonly IUnitOfWork _uow;
    private readonly ICompanyContext _company;
    private readonly ICurrentUserService _user;
    private readonly IDateTimeProvider _clock;
    private readonly IMemoryCache _cache;

    public ConfigurationTransferService(IUnitOfWork uow, ICompanyContext company, ICurrentUserService user, IDateTimeProvider clock, IMemoryCache cache)
    {
        _uow = uow;
        _company = company;
        _user = user;
        _clock = clock;
        _cache = cache;
    }

    private int CurrentCompanyId => _company.CompanyId ?? throw new InvalidOperationException("No company is active.");

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default) =>
        ConfigurationXml.Write(await ReadAsync(CurrentCompanyId, cancellationToken));

    public async Task<Result<ConfigPreview>> PreviewImportAsync(Stream xml, CancellationToken cancellationToken = default)
    {
        var (document, error) = ConfigurationXml.Read(xml);
        if (document is null) return Result<ConfigPreview>.Failure(Error.Validation(error ?? "The file could not be read.", "file"));
        var source = string.IsNullOrWhiteSpace(document.SourceCompanyName) ? "uploaded file" : $"file exported from {document.SourceCompanyName}";
        return Result<ConfigPreview>.Success(await BuildPreviewAsync(document, source, cancellationToken));
    }

    public async Task<Result<ConfigPreview>> PreviewCopyAsync(int sourceCompanyId, CancellationToken cancellationToken = default)
    {
        if (sourceCompanyId == CurrentCompanyId) return Result<ConfigPreview>.Failure(Error.Validation("Choose a different company to copy from.", "source"));
        var source = await _uow.Repository<Company>().FirstOrDefaultAsync(c => c.CompanyId == sourceCompanyId && c.Status == CompanyStatuses.Active, cancellationToken);
        if (source is null) return Result<ConfigPreview>.Failure(Error.NotFound("That company is not available."));
        var document = await ReadAsync(sourceCompanyId, cancellationToken);
        return Result<ConfigPreview>.Success(await BuildPreviewAsync(document, "company " + source.DisplayName, cancellationToken));
    }

    private async Task<ConfigPreview> BuildPreviewAsync(ConfigurationDocument incoming, string source, CancellationToken ct)
    {
        var current = await ReadAsync(CurrentCompanyId, ct);
        var changes = ConfigurationDiff.Compute(current, incoming);
        var token = Guid.NewGuid().ToString("N");
        var expires = _clock.UtcNow.Add(PreviewLifetime);
        _cache.Set(CacheKey(token), new Pending(_user.UserName, CurrentCompanyId, incoming, changes, source), PreviewLifetime);
        return new ConfigPreview(token, source, changes, expires);
    }

    private static string CacheKey(string token) => "config-import:" + token;

    // ---------------------------------------------------------------- read the settings of one company
    private async Task<ConfigurationDocument> ReadAsync(int companyId, CancellationToken ct)
    {
        // another company is read inside its own company scope, so Row-Level Security and the query filters apply to it
        using var scope = companyId == _company.CompanyId ? null : _company.Enter(companyId);
        var company = await _uow.Repository<Company>().FirstOrDefaultAsync(c => c.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("Company not found.");
        var sequences = (await _uow.Repository<AssetNumberSequence>().QueryAsync(q => q.Select(s => new { s.AssetNumberSequenceId, s.SequenceCode, s.PaddingLength }), ct))
            .ToDictionary(s => s.AssetNumberSequenceId);
        var cats = await _uow.Repository<AssetCategory>().QueryAsync(q => q.OrderBy(c => c.SortOrder).ThenBy(c => c.CategoryCode).Select(c => new
        {
            c.AssetCategoryId, c.CategoryCode, c.CategoryName, c.AssetNumberSequenceId, c.DefaultUsefulLifeMonths, c.RequiresHostname, c.Description, c.SortOrder, c.IsActive
        }), ct);
        var subs = await _uow.Repository<AssetSubCategory>().QueryAsync(q => q.OrderBy(s => s.SortOrder).ThenBy(s => s.SubCategoryCode).Select(s => new
        {
            s.AssetCategoryId, s.SubCategoryCode, s.SubCategoryName, s.Description, s.SortOrder, s.IsActive
        }), ct);
        var classifications = await _uow.Repository<Classification>().QueryAsync(q => q.OrderBy(c => c.ClassificationLevel).ThenBy(c => c.ClassificationCode).Select(c => new
        {
            c.ClassificationCode, c.ClassificationName, c.ClassificationLevel, c.HandlingRequirements, c.ColorHex, c.Description, c.SortOrder, c.IsActive
        }), ct);
        var locations = await _uow.Repository<Location>().QueryAsync(q => q.OrderBy(l => l.LocationCode).Select(l => new
        {
            l.LocationCode, l.LocationName, l.AddressLine, l.City, l.Country, l.IsActive
        }), ct);

        return new ConfigurationDocument(company.CompanyCode, company.DisplayName, _clock.UtcNow,
            cats.Select(c => new CategoryConfig(c.CategoryCode, c.CategoryName,
                sequences.TryGetValue(c.AssetNumberSequenceId, out var s) ? s.SequenceCode : "XX", sequences.TryGetValue(c.AssetNumberSequenceId, out var s2) ? s2.PaddingLength : (byte)5,
                c.DefaultUsefulLifeMonths, c.RequiresHostname, c.Description, c.SortOrder, c.IsActive,
                subs.Where(x => x.AssetCategoryId == c.AssetCategoryId).Select(x => new SubCategoryConfig(x.SubCategoryCode, x.SubCategoryName, x.Description, x.SortOrder, x.IsActive)).ToList())).ToList(),
            classifications.Select(c => new ClassificationConfig(c.ClassificationCode, c.ClassificationName, c.ClassificationLevel, c.HandlingRequirements, c.ColorHex?.Trim(), c.Description, c.SortOrder, c.IsActive)).ToList(),
            new NumberingConfig(company.DocNumberingMode, company.FiscalYearStartMonth, company.DocYearFormat, company.DocNumberDigits),
            locations.Select(l => new LocationConfig(l.LocationCode, l.LocationName, l.AddressLine, l.City, l.Country, l.IsActive)).ToList());
    }

    // ---------------------------------------------------------------- commit
    public async Task<Result<ConfigImportResult>> CommitAsync(string token, IReadOnlyCollection<string> selectedKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedKeys);
        var companyId = CurrentCompanyId;
        if (string.IsNullOrWhiteSpace(token) || !_cache.TryGetValue(CacheKey(token), out Pending? pending) || pending is null)
            return Result<ConfigImportResult>.Failure(Error.NotFound("This preview has expired. Upload the file again."));
        if (pending.CompanyId != companyId || !string.Equals(pending.UserName, _user.UserName, StringComparison.OrdinalIgnoreCase))
            return Result<ConfigImportResult>.Failure(Error.Forbidden("This preview belongs to another session."));
        _cache.Remove(CacheKey(token)); // one use

        // only rows that were shown as additions or modifications can be selected; anything else the browser sent is ignored
        var allowed = pending.Changes.Where(c => c.Kind != ChangeKind.Unchanged).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var selected = selectedKeys.Where(allowed.Contains).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0) return Result<ConfigImportResult>.Failure(Error.Validation("Tick at least one change to apply."));

        int added = 0, modified = 0, skipped = 0;
        await _uow.ExecuteInTransactionAsync(async ct =>
        {
            var company = await _uow.Repository<Company>().FirstOrDefaultAsync(c => c.CompanyId == companyId, ct)
                ?? throw new InvalidOperationException("Company not found.");
            var doc = pending.Document;
            var kinds = pending.Changes.ToDictionary(c => c.Key, c => c.Kind, StringComparer.Ordinal);

            // ----- categories (and the number series they need)
            var sequenceRepo = _uow.Repository<AssetNumberSequence>();
            var sequences = await sequenceRepo.ListAsync(null, ct);
            var categoryRepo = _uow.Repository<AssetCategory>();
            var categories = await categoryRepo.ListAsync(null, ct);
            foreach (var c in doc.Categories)
            {
                var key = "category:" + c.Code.ToUpperInvariant();
                if (!selected.Contains(key)) continue;
                var existing = categories.FirstOrDefault(x => string.Equals(x.CategoryCode, c.Code, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    var sequence = sequences.FirstOrDefault(s => string.Equals(s.SequenceCode, c.SequenceCode, StringComparison.OrdinalIgnoreCase));
                    if (sequence is null)
                    {
                        sequence = new AssetNumberSequence { SequenceCode = c.SequenceCode, Prefix = company.AssetNumberPrefix, PaddingLength = c.SequencePadding, Description = c.Name, SortOrder = c.SortOrder, IsActive = true };
                        await sequenceRepo.AddAsync(sequence, ct);
                        await _uow.SaveChangesAsync(ct);
                        sequences.Add(sequence);
                    }
                    var created = new AssetCategory
                    {
                        CategoryCode = c.Code, CategoryName = c.Name, AssetNumberSequenceId = sequence.AssetNumberSequenceId, DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths,
                        RequiresHostname = c.RequiresHostname, Description = c.Description, SortOrder = c.SortOrder, IsActive = c.IsActive
                    };
                    await categoryRepo.AddAsync(created, ct);
                    categories.Add(created);
                    added++;
                }
                else
                {
                    existing.CategoryName = c.Name; existing.DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths; existing.RequiresHostname = c.RequiresHostname;
                    existing.Description = c.Description; existing.SortOrder = c.SortOrder; existing.IsActive = c.IsActive;
                    modified++;
                }
            }
            await _uow.SaveChangesAsync(ct);

            // ----- sub-categories: their category must exist by now (already there, or ticked in the same import)
            var subRepo = _uow.Repository<AssetSubCategory>();
            var existingSubs = await subRepo.ListAsync(null, ct);
            foreach (var c in doc.Categories)
            {
                foreach (var s in c.SubCategories)
                {
                    var key = $"subcategory:{c.Code.ToUpperInvariant()}/{s.Code.ToUpperInvariant()}";
                    if (!selected.Contains(key)) continue;
                    var parent = categories.FirstOrDefault(x => string.Equals(x.CategoryCode, c.Code, StringComparison.OrdinalIgnoreCase));
                    if (parent is null) { skipped++; continue; }
                    var existing = existingSubs.FirstOrDefault(x => x.AssetCategoryId == parent.AssetCategoryId && string.Equals(x.SubCategoryCode, s.Code, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        await subRepo.AddAsync(new AssetSubCategory { AssetCategoryId = parent.AssetCategoryId, SubCategoryCode = s.Code, SubCategoryName = s.Name, Description = s.Description, SortOrder = s.SortOrder, IsActive = s.IsActive }, ct);
                        added++;
                    }
                    else
                    {
                        existing.SubCategoryName = s.Name; existing.Description = s.Description; existing.SortOrder = s.SortOrder; existing.IsActive = s.IsActive;
                        modified++;
                    }
                }
            }

            // ----- classifications
            var classRepo = _uow.Repository<Classification>();
            var classifications = await classRepo.ListAsync(null, ct);
            foreach (var c in doc.Classifications)
            {
                if (!selected.Contains("classification:" + c.Code.ToUpperInvariant())) continue;
                var existing = classifications.FirstOrDefault(x => string.Equals(x.ClassificationCode, c.Code, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    await classRepo.AddAsync(new Classification { ClassificationCode = c.Code, ClassificationName = c.Name, ClassificationLevel = c.Level, HandlingRequirements = c.HandlingRequirements,
                        ColorHex = c.ColorHex, Description = c.Description, SortOrder = c.SortOrder, IsActive = c.IsActive }, ct);
                    added++;
                }
                else
                {
                    existing.ClassificationName = c.Name; existing.ClassificationLevel = c.Level; existing.HandlingRequirements = c.HandlingRequirements; existing.ColorHex = c.ColorHex;
                    existing.Description = c.Description; existing.SortOrder = c.SortOrder; existing.IsActive = c.IsActive;
                    modified++;
                }
            }

            // ----- document numbering (the settings only; counters are never touched)
            if (doc.Numbering is { } n && selected.Contains("numbering:company"))
            {
                company.DocNumberingMode = n.Mode; company.FiscalYearStartMonth = n.FiscalYearStartMonth; company.DocYearFormat = n.YearFormat; company.DocNumberDigits = n.Digits;
                _uow.Repository<Company>().Update(company);
                modified++;
            }

            // ----- locations
            var locationRepo = _uow.Repository<Location>();
            var locations = await locationRepo.ListAsync(null, ct);
            foreach (var l in doc.Locations)
            {
                if (!selected.Contains("location:" + l.Code.ToUpperInvariant())) continue;
                var existing = locations.FirstOrDefault(x => string.Equals(x.LocationCode, l.Code, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    await locationRepo.AddAsync(new Location { LocationCode = l.Code, LocationName = l.Name, AddressLine = l.AddressLine, City = l.City, Country = l.Country, IsActive = l.IsActive }, ct);
                    added++;
                }
                else
                {
                    existing.LocationName = l.Name; existing.AddressLine = l.AddressLine; existing.City = l.City; existing.Country = l.Country; existing.IsActive = l.IsActive;
                    modified++;
                }
            }

            await _uow.Repository<AuditLog>().AddAsync(new AuditLog
            {
                EntityName = "CompanyConfiguration", EntityKey = companyId.ToString(CultureInfo.InvariantCulture), Action = AuditAction.Import,
                NewValues = JsonSerializer.Serialize(new { source = pending.Source, added, modified, skipped, rows = kinds.Count(k => selected.Contains(k.Key)) }),
                PerformedBy = _user.UserName, IpAddress = _user.IpAddress, CorrelationId = _user.CorrelationId, EventDate = _clock.UtcNow
            }, ct);
            await _uow.SaveChangesAsync(ct);
        }, cancellationToken);
        return Result<ConfigImportResult>.Success(new ConfigImportResult(added, modified, skipped));
    }
}
