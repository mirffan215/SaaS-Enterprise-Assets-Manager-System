using System.Text.RegularExpressions;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Passwords;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Errors;
using Microsoft.Extensions.Logging;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Security.Platform;

/// <summary>Platform Super Administrator: create, edit, suspend and archive company workspaces.</summary>
public interface ICompanyAdministrationService
{
    /// <summary>Company whose lists (categories, number series, classifications, roles…) new companies start from.</summary>
    const string TemplateCompanyCode = "AMGH";

    Task<IReadOnlyList<CompanyListItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<CompanyDetailsForm?> GetAsync(int companyId, CancellationToken cancellationToken = default);

    /// <summary>Creates and provisions a company in one go (the setup wizard).</summary>
    Task<Result<CompanySetupResult>> CreateAsync(CompanySetupRequest request, CompanyLogo? logo, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(int companyId, CompanyDetailsForm form, CompanyLogo? newLogo, CancellationToken cancellationToken = default);
    Task<Result> SetStatusAsync(int companyId, string status, CancellationToken cancellationToken = default);

    /// <summary>Re-runs the copying of base lists for a company whose setup was interrupted (idempotent).</summary>
    Task<Result<IReadOnlyList<string>>> CompleteSetupAsync(int companyId, CancellationToken cancellationToken = default);
}

internal sealed partial class CompanyAdministrationService : ICompanyAdministrationService
{
    public const int MaxLogoBytes = 512 * 1024;

    [GeneratedRegex("^[A-Z0-9]{2,10}$")] private static partial Regex CodePattern();
    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,19}$")] private static partial Regex PrefixPattern();
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex ColorPattern();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailPattern();
    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,100}$")] private static partial Regex UsernamePattern();

    private readonly IUnitOfWork _uow;
    private readonly ICompanyContext _company;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly AccountService _accounts;
    private readonly IPasswordPolicy _policy;
    private readonly SecurityOptions _options;
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;

    public CompanyAdministrationService(IUnitOfWork uow, ICompanyContext company, ICurrentUserService currentUser,
        IDateTimeProvider clock, AccountService accounts, IPasswordPolicy policy, IOptions<SecurityOptions> options,
        Microsoft.Extensions.Logging.ILogger<CompanyAdministrationService>? logger = null)
    {
        _logger = logger;
        _uow = uow;
        _company = company;
        _currentUser = currentUser;
        _clock = clock;
        _accounts = accounts;
        _policy = policy;
        _options = options.Value;
    }

    // ------------------------------------------------------------------ queries

    public async Task<IReadOnlyList<CompanyListItem>> ListAsync(CancellationToken cancellationToken = default) =>
        await _uow.Repository<Company>().QueryAsync(q => q.OrderBy(c => c.DisplayName).Select(c => new CompanyListItem(
            c.CompanyId, c.CompanyCode, c.DisplayName, c.LegalName, c.Status,
            c.Members.Count(m => m.IsActive), c.CreatedDate, c.ModifiedBy != SetupPendingMarker)), cancellationToken);

    public async Task<CompanyDetailsForm?> GetAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var rows = await _uow.Repository<Company>().QueryAsync(q => q.Where(c => c.CompanyId == companyId).Select(c => new { c, c.Branding }), cancellationToken);
        if (rows.Count == 0) return null;
        var (x, b) = (rows[0].c, rows[0].Branding);
        return new CompanyDetailsForm
        {
            CompanyCode = x.CompanyCode, LegalName = x.LegalName, DisplayName = x.DisplayName, RegistrationNo = x.RegistrationNo,
            Industry = x.Industry, Email = x.Email, Phone = x.Phone, Website = x.Website, Country = x.Country, City = x.City,
            AddressLine = x.AddressLine, TimeZone = x.TimeZone, LanguageCode = x.LanguageCode, BaseCurrencyId = x.BaseCurrencyId,
            NameArabic = x.NameArabic, ParentCompanyId = x.ParentCompanyId, CompanyType = x.CompanyType, VatNumber = x.VatNumber, Region = x.Region,
            DateFormat = x.DateFormat, NotificationsEnabled = x.NotificationsEnabled, AssetAuditEnabled = x.AssetAuditEnabled,
            DocNumberingMode = x.DocNumberingMode, FiscalYearStartMonth = x.FiscalYearStartMonth, DocYearFormat = x.DocYearFormat, DocNumberDigits = x.DocNumberDigits,
            LogoSourceUrl = b == null ? null : b.LogoSourceUrl,
            AssetNumberPrefix = x.AssetNumberPrefix,
            HeaderColor = b?.HeaderColor ?? BrandingDefaults.Header, PrimaryColor = b?.PrimaryColor ?? BrandingDefaults.Primary,
            AccentColor = b?.AccentColor ?? BrandingDefaults.Accent, LoginTagline = b?.LoginTagline,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        };
    }

    // ------------------------------------------------------------------ create (wizard)

    /// <summary>Written to ModifiedBy while the base lists are not yet copied; cleared when setup completes.</summary>
    private const string SetupPendingMarker = "SETUP-PENDING";

    private async Task<Result<CompanySetupResult>> CreateCoreAsync(CompanySetupRequest request, CompanyLogo? logo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ct = cancellationToken;
        var d = Normalize(request.Details);
        var errors = ValidateDetails(d, logo).ToList();
        if (await ParentProblemAsync(null, d.ParentCompanyId, cancellationToken) is { } parentProblem) errors.Add(Error.Validation(parentProblem, "Details.ParentCompanyId"));
        if (await _uow.Repository<Company>().AnyAsync(c => c.CompanyCode == d.CompanyCode, ct))
            errors.Add(Error.Conflict("Another company already uses this code.", "Details.CompanyCode"));
        if (await _uow.Repository<Company>().AnyAsync(c => c.AssetNumberPrefix == d.AssetNumberPrefix, ct))
            errors.Add(Error.Conflict("Another company already uses this asset number prefix.", "Details.AssetNumberPrefix"));

        // administrator
        User? existingAdmin = null;
        string adminUsername;
        if (request.AdminMode == "existing")
        {
            adminUsername = (request.ExistingUsername ?? string.Empty).Trim();
            existingAdmin = await _uow.Repository<User>().FirstOrDefaultAsync(u => u.Username == adminUsername && u.IsActive, ct);
            if (existingAdmin is null) errors.Add(Error.Validation("No active user with this username.", "ExistingUsername"));
        }
        else
        {
            adminUsername = (request.NewUsername ?? string.Empty).Trim();
            var email = (request.NewEmail ?? string.Empty).Trim();
            if (!UsernamePattern().IsMatch(adminUsername))
                errors.Add(Error.Validation("Username: 3-100 letters, digits, '.', '_' or '-'.", "NewUsername"));
            else if (await _uow.Repository<User>().AnyAsync(u => u.Username == adminUsername, ct))
                errors.Add(Error.Conflict("This username is taken. Choose \"existing user\" to give that person access.", "NewUsername"));
            if ((request.NewDisplayName ?? string.Empty).Trim().Length is < 2 or > 200)
                errors.Add(Error.Validation("Enter the administrator's name.", "NewDisplayName"));
            if (!EmailPattern().IsMatch(email))
                errors.Add(Error.Validation("Enter a valid e-mail address.", "NewEmail"));
            else if (await _uow.Repository<User>().AnyAsync(u => u.Email == email, ct))
                errors.Add(Error.Conflict("Another user already has this e-mail address.", "NewEmail"));
        }
        if (request.HeadOfficeName?.Trim().Length > 150) errors.Add(Error.Validation("Head office name: up to 150 characters.", "HeadOfficeName"));
        if (request.FirstDepartmentName?.Trim().Length > 150) errors.Add(Error.Validation("Department name: up to 150 characters.", "FirstDepartmentName"));
        var templateId = await TemplateCompanyIdAsync(ct);
        if (templateId is null) errors.Add(Error.BusinessRule($"The template company {ICompanyAdministrationService.TemplateCompanyCode} was not found."));
        if (errors.Count > 0) return Result<CompanySetupResult>.Failure(errors.ToArray());

        // 1) the company row (platform table) - marked "setup pending" until the lists are copied
        var now = _clock.UtcNow;
        var company = new Company { Status = CompanyStatuses.Suspended, CreatedBy = _currentUser.UserName, CreatedDate = now, ModifiedBy = SetupPendingMarker, ModifiedDate = now };
        Apply(company, d);
        var branding = new CompanyBranding { Company = company, ModifiedBy = _currentUser.UserName, ModifiedDate = now };
        ApplyBranding(branding, d, logo);
        await _uow.Repository<Company>().AddAsync(company, ct);
        await _uow.Repository<CompanyBranding>().AddAsync(branding, ct);
        await SavePlatformAsync(ct);

        // 2) base lists, first structure and administrator - inside the new company, in one transaction
        string? temporaryPassword = null;
        IReadOnlyList<string> copied;
        try
        {
            var template = await ReadTemplateAsync(templateId!.Value, ct);
            using (_company.Enter(company.CompanyId))
            {
                copied = await _uow.ExecuteInTransactionAsync(async tct =>
                {
                    var summary = await CopyBaseListsAsync(template, d.AssetNumberPrefix, tct);
                    await CreateStructureAsync(request, d, summary, tct);
                    temporaryPassword = await CreateAdministratorAsync(company.CompanyId, existingAdmin, request, adminUsername, tct);
                    return summary.Lines;
                }, ct);
            }
        }
        catch (Exception ex) when (ex is BusinessRuleException or DataIntegrityException or DuplicateValueException or InvalidOperationException)
        {
            return Result<CompanySetupResult>.Failure(Error.BusinessRule(
                $"The company {d.CompanyCode} was created but its setup did not finish ({ex.Message}). Open it under Companies and choose \"Complete setup\"."));
        }

        // 3) activate
        company.Status = CompanyStatuses.Active;
        company.ModifiedBy = _currentUser.UserName;
        company.ModifiedDate = _clock.UtcNow;
        await SavePlatformAsync(ct);
        return Result<CompanySetupResult>.Success(new CompanySetupResult(company.CompanyId, company.CompanyCode, adminUsername, temporaryPassword, copied));
    }

    public async Task<Result<IReadOnlyList<string>>> CompleteSetupAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var company = await _uow.Repository<Company>().FirstOrDefaultAsync(c => c.CompanyId == companyId, ct);
        if (company is null) return Result<IReadOnlyList<string>>.Failure(Error.NotFound("Company not found."));
        if (company.ModifiedBy != SetupPendingMarker) return Result<IReadOnlyList<string>>.Failure(Error.BusinessRule("This company's setup is already complete."));
        var templateId = await TemplateCompanyIdAsync(ct);
        if (templateId is null) return Result<IReadOnlyList<string>>.Failure(Error.BusinessRule("The template company was not found."));

        var template = await ReadTemplateAsync(templateId.Value, ct);
        IReadOnlyList<string> copied;
        using (_company.Enter(company.CompanyId))
            copied = await _uow.ExecuteInTransactionAsync(async tct => (await CopyBaseListsAsync(template, company.AssetNumberPrefix, tct)).Lines, ct);
        company.Status = CompanyStatuses.Active;
        company.ModifiedBy = _currentUser.UserName;
        company.ModifiedDate = _clock.UtcNow;
        await SavePlatformAsync(ct);
        return Result<IReadOnlyList<string>>.Success(copied);
    }

    // ------------------------------------------------------------------ edit / status

    private async Task<Result> UpdateCoreAsync(int companyId, CompanyDetailsForm form, CompanyLogo? newLogo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var ct = cancellationToken;
        var company = await _uow.Repository<Company>().FirstOrDefaultAsync(c => c.CompanyId == companyId, ct);
        if (company is null) return Result.Failure(Error.NotFound("Company not found."));
        var d = Normalize(form);
        d.CompanyCode = company.CompanyCode;           // the code identifies the workspace (URLs, labels): fixed
        d.AssetNumberPrefix = company.AssetNumberPrefix; // printed on existing tags: fixed
        var errors = ValidateDetails(d, newLogo).ToList();
        if (await ParentProblemAsync(companyId, d.ParentCompanyId, ct) is { } parentProblem) errors.Add(Error.Validation(parentProblem, "Details.ParentCompanyId"));
        if (errors.Count > 0) return Result.Failure(errors.ToArray());

        if (!string.IsNullOrEmpty(form.RowVersion))
        {
            try { _uow.Repository<Company>().SetOriginalRowVersion(company, Convert.FromBase64String(form.RowVersion)); }
            catch (FormatException) { return Result.Failure(Error.Validation("The form is out of date. Reload and try again.")); }
        }
        Apply(company, d);
        company.ModifiedBy = company.ModifiedBy == SetupPendingMarker ? SetupPendingMarker : _currentUser.UserName;
        company.ModifiedDate = _clock.UtcNow;

        var branding = await _uow.Repository<CompanyBranding>().FirstOrDefaultAsync(b => b.CompanyId == companyId, ct);
        if (branding is null)
        {
            branding = new CompanyBranding { CompanyId = companyId };
            await _uow.Repository<CompanyBranding>().AddAsync(branding, ct);
        }
        ApplyBranding(branding, d, newLogo);
        if (form.RemoveLogo && newLogo is null) { branding.LogoImage = null; branding.LogoContentType = null; }
        branding.ModifiedBy = _currentUser.UserName;
        branding.ModifiedDate = _clock.UtcNow;
        await SavePlatformAsync(ct);
        return Result.Success();
    }

    private async Task<Result> SetStatusCoreAsync(int companyId, string status, CancellationToken cancellationToken)
    {
        if (status is not (CompanyStatuses.Active or CompanyStatuses.Suspended or CompanyStatuses.Archived))
            return Result.Failure(Error.Validation("Unknown status."));
        var company = await _uow.Repository<Company>().FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken);
        if (company is null) return Result.Failure(Error.NotFound("Company not found."));
        if (company.ModifiedBy == SetupPendingMarker && status == CompanyStatuses.Active)
            return Result.Failure(Error.BusinessRule("Complete the company's setup first."));
        if (companyId == _company.CompanyId && status != CompanyStatuses.Active)
            return Result.Failure(Error.BusinessRule("You are signed in to this company. Switch to another company before suspending or archiving it."));
        if (company.CompanyCode == ICompanyAdministrationService.TemplateCompanyCode && status != CompanyStatuses.Active)
            return Result.Failure(Error.BusinessRule("The template company cannot be suspended or archived."));
        company.Status = status;
        company.ModifiedBy = _currentUser.UserName;
        company.ModifiedDate = _clock.UtcNow;
        await SavePlatformAsync(cancellationToken);
        return Result.Success();
    }


    // ------------------------------------------------------------------ guarded public operations (Release 2.7)
    // Database refusals become clear messages instead of the generic error page; the full exception is still logged
    // (with the request's trace id) so it can be found from the reference the user quotes.

    public Task<Result<CompanySetupResult>> CreateAsync(CompanySetupRequest request, CompanyLogo? logo, CancellationToken cancellationToken = default) =>
        GuardAsync("create", request?.Details?.CompanyCode, () => CreateCoreAsync(request!, logo, cancellationToken), e => Result<CompanySetupResult>.Failure(e));

    public Task<Result> UpdateAsync(int companyId, CompanyDetailsForm form, CompanyLogo? newLogo, CancellationToken cancellationToken = default) =>
        GuardAsync("update", companyId.ToString(System.Globalization.CultureInfo.InvariantCulture), () => UpdateCoreAsync(companyId, form, newLogo, cancellationToken), e => Result.Failure(e));

    public Task<Result> SetStatusAsync(int companyId, string status, CancellationToken cancellationToken = default) =>
        GuardAsync("status", companyId.ToString(System.Globalization.CultureInfo.InvariantCulture), () => SetStatusCoreAsync(companyId, status, cancellationToken), e => Result.Failure(e));

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "Company {Operation} {Company}: changed by someone else")]
    private static partial void LogConflict(ILogger logger, Exception exception, string operation, string? company);
    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning, Message = "Company {Operation} {Company}: duplicate value")]
    private static partial void LogDuplicate(ILogger logger, Exception exception, string operation, string? company);
    [LoggerMessage(EventId = 7103, Level = LogLevel.Error, Message = "Company {Operation} {Company}: refused by the database")]
    private static partial void LogRefused(ILogger logger, Exception exception, string operation, string? company);
    [LoggerMessage(EventId = 7104, Level = LogLevel.Error, Message = "Company {Operation} {Company}: company-context safety check")]
    private static partial void LogContextCheck(ILogger logger, Exception exception, string operation, string? company);

    private async Task<T> GuardAsync<T>(string operation, string? company, Func<Task<T>> work, Func<Error, T> fail)
    {
        try
        {
            return await work();
        }
        catch (ConcurrencyConflictException ex)
        {
            if (_logger is not null) LogConflict(_logger, ex, operation, company);
            return fail(Error.Conflict("Someone else changed this company while you were editing. Reload the page and apply your changes again."));
        }
        catch (DuplicateValueException ex)
        {
            if (_logger is not null) LogDuplicate(_logger, ex, operation, company);
            return fail(Error.Conflict("A value must be unique but is already used (for example the company code or the number prefix). " + ex.Message));
        }
        catch (DataIntegrityException ex)
        {
            if (_logger is not null) LogRefused(_logger, ex, operation, company);
            return fail(Error.BusinessRule("The database refused the change: " + ex.Message));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Cannot create", StringComparison.Ordinal) || ex.Message.StartsWith("The company of", StringComparison.Ordinal))
        {
            if (_logger is not null) LogContextCheck(_logger, ex, operation, company);
            return fail(Error.BusinessRule("The change could not be saved (company context check). The details are in the application log."));
        }
    }

    /// <summary>The parent must exist and must not be the company itself or one of its own subsidiaries (no loops).</summary>
    private async Task<string?> ParentProblemAsync(int? companyId, int? parentId, CancellationToken ct)
    {
        if (parentId is null) return null;
        if (parentId == companyId) return "A company cannot be its own parent.";
        var parents = (await _uow.Repository<Company>().QueryAsync(q => q.Select(c => new { c.CompanyId, c.ParentCompanyId }), ct))
            .ToDictionary(c => c.CompanyId, c => c.ParentCompanyId);
        if (!parents.ContainsKey(parentId.Value)) return "The parent company does not exist.";
        var seen = new HashSet<int>();
        for (int? p = parentId; p is { } cur && seen.Add(cur); p = parents.GetValueOrDefault(cur))
            if (cur == companyId) return "That would make a loop: the chosen parent belongs to this company.";
        return null;
    }

    /// <summary>Company records are platform data: their audit entries are platform events (no company).</summary>
    private async Task SavePlatformAsync(CancellationToken ct)
    {
        using var scope = _company.EnterPlatform();
        await _uow.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ provisioning

    private sealed record TemplateData(
        List<AssetNumberSequence> Sequences, List<AssetCategory> Categories, List<AssetSubCategory> SubCategories,
        List<Classification> Classifications, List<RiskLevel> RiskLevels, List<Criticality> Criticalities,
        List<Manufacturer> Manufacturers, List<Role> Roles, List<(int RoleId, int PermissionId)> RolePermissions);

    private sealed class CopySummary
    {
        public List<string> Lines { get; } = new();
        public Dictionary<string, Role> RolesByName { get; } = new(StringComparer.Ordinal);
    }

    private async Task<int?> TemplateCompanyIdAsync(CancellationToken ct)
    {
        var ids = await _uow.Repository<Company>().QueryAsync(q => q
            .Where(c => c.CompanyCode == ICompanyAdministrationService.TemplateCompanyCode).Select(c => c.CompanyId), ct);
        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>Reads the template company's lists (no tracking) while working in the template company.</summary>
    private async Task<TemplateData> ReadTemplateAsync(int templateId, CancellationToken ct)
    {
        using var scope = _company.Enter(templateId);
        return new TemplateData(
            await _uow.Repository<AssetNumberSequence>().QueryAsync(q => q.Where(x => x.IsActive), ct),
            await _uow.Repository<AssetCategory>().QueryAsync(q => q.Where(x => x.IsActive), ct),
            await _uow.Repository<AssetSubCategory>().QueryAsync(q => q.Where(x => x.IsActive), ct),
            await _uow.Repository<Classification>().QueryAsync(q => q.Where(x => x.IsActive), ct),
            await _uow.Repository<RiskLevel>().QueryAsync(q => q.Where(x => x.IsActive), ct),
            await _uow.Repository<Criticality>().QueryAsync(q => q.Where(x => x.IsActive), ct),
            await _uow.Repository<Manufacturer>().QueryAsync(q => q.Where(x => x.IsActive), ct),
            await _uow.Repository<Role>().QueryAsync(q => q.Where(x => x.IsActive), ct),
            (await _uow.Repository<RolePermission>().QueryAsync(q => q.Select(x => new { x.RoleId, x.PermissionId }), ct))
                .Select(x => (x.RoleId, x.PermissionId)).ToList());
    }

    /// <summary>
    /// Copies the template's lists into the current (new) company. Idempotent: entries whose code or name already
    /// exist are skipped, so an interrupted setup can be completed. Number series start at 1 with the new prefix.
    /// </summary>
    private async Task<CopySummary> CopyBaseListsAsync(TemplateData t, string prefix, CancellationToken ct)
    {
        var summary = new CopySummary();

        // number series -> categories -> sub-categories (each level saved so the next can reference it)
        var seqRepo = _uow.Repository<AssetNumberSequence>();
        var existingSeq = (await seqRepo.ListAsync(null, ct)).ToDictionary(s => s.SequenceCode, StringComparer.Ordinal);
        var seqMap = new Dictionary<int, AssetNumberSequence>();
        var added = 0;
        foreach (var s in t.Sequences)
        {
            if (!existingSeq.TryGetValue(s.SequenceCode, out var target))
            {
                target = new AssetNumberSequence { SequenceCode = s.SequenceCode, Prefix = prefix, LastNumber = 0, PaddingLength = s.PaddingLength,
                    Description = s.Description, SortOrder = s.SortOrder, IsActive = true };
                await seqRepo.AddAsync(target, ct);
                added++;
            }
            seqMap[s.AssetNumberSequenceId] = target;
        }
        await _uow.SaveChangesAsync(ct);
        summary.Lines.Add($"{added} number series (prefix {prefix})");

        var catRepo = _uow.Repository<AssetCategory>();
        var existingCat = (await catRepo.ListAsync(null, ct)).ToDictionary(c => c.CategoryCode, StringComparer.Ordinal);
        var catMap = new Dictionary<int, AssetCategory>();
        added = 0;
        foreach (var c in t.Categories)
        {
            if (!existingCat.TryGetValue(c.CategoryCode, out var target))
            {
                if (!seqMap.TryGetValue(c.AssetNumberSequenceId, out var seq)) continue;
                target = new AssetCategory { CategoryCode = c.CategoryCode, CategoryName = c.CategoryName, AssetNumberSequenceId = seq.AssetNumberSequenceId,
                    DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths, RequiresHostname = c.RequiresHostname, Description = c.Description,
                    SortOrder = c.SortOrder, IsActive = true };
                await catRepo.AddAsync(target, ct);
                added++;
            }
            catMap[c.AssetCategoryId] = target;
        }
        await _uow.SaveChangesAsync(ct);
        summary.Lines.Add($"{added} asset categories");

        var subRepo = _uow.Repository<AssetSubCategory>();
        var existingSub = (await subRepo.ListAsync(null, ct)).Select(x => (x.AssetCategoryId, x.SubCategoryCode)).ToHashSet();
        added = 0;
        foreach (var s in t.SubCategories)
        {
            if (!catMap.TryGetValue(s.AssetCategoryId, out var cat) || existingSub.Contains((cat.AssetCategoryId, s.SubCategoryCode))) continue;
            await subRepo.AddAsync(new AssetSubCategory { AssetCategoryId = cat.AssetCategoryId, SubCategoryCode = s.SubCategoryCode,
                SubCategoryName = s.SubCategoryName, Description = s.Description, SortOrder = s.SortOrder, IsActive = true }, ct);
            added++;
        }
        summary.Lines.Add($"{added} sub-categories");

        added = await CopyAsync(t.Classifications, x => x.ClassificationCode, x => new Classification { ClassificationCode = x.ClassificationCode,
            ClassificationName = x.ClassificationName, ClassificationLevel = x.ClassificationLevel, HandlingRequirements = x.HandlingRequirements,
            ColorHex = x.ColorHex, Description = x.Description, SortOrder = x.SortOrder, IsActive = true }, ct);
        summary.Lines.Add($"{added} information classifications");
        added = await CopyAsync(t.RiskLevels, x => x.RiskLevelCode, x => new RiskLevel { RiskLevelCode = x.RiskLevelCode, RiskLevelName = x.RiskLevelName,
            MinScore = x.MinScore, MaxScore = x.MaxScore, ReviewFrequencyMonths = x.ReviewFrequencyMonths, ColorHex = x.ColorHex,
            Description = x.Description, SortOrder = x.SortOrder, IsActive = true }, ct);
        summary.Lines.Add($"{added} risk levels");
        added = await CopyAsync(t.Criticalities, x => x.CriticalityCode, x => new Criticality { CriticalityCode = x.CriticalityCode,
            CriticalityName = x.CriticalityName, CriticalityLevel = x.CriticalityLevel, RtoHours = x.RtoHours, RpoHours = x.RpoHours,
            Description = x.Description, SortOrder = x.SortOrder, IsActive = true }, ct);
        summary.Lines.Add($"{added} criticality levels");
        added = await CopyAsync(t.Manufacturers, x => x.ManufacturerCode, x => new Manufacturer { ManufacturerCode = x.ManufacturerCode,
            ManufacturerName = x.ManufacturerName, Website = x.Website, SupportEmail = x.SupportEmail, SupportPhone = x.SupportPhone, IsActive = true }, ct);
        summary.Lines.Add($"{added} manufacturers");

        // roles, then their permissions
        var roleRepo = _uow.Repository<Role>();
        var existingRoles = (await roleRepo.ListAsync(null, ct)).ToDictionary(r => r.RoleName, StringComparer.Ordinal);
        var roleMap = new Dictionary<int, Role>();
        added = 0;
        foreach (var r in t.Roles)
        {
            if (!existingRoles.TryGetValue(r.RoleName, out var target))
            {
                target = new Role { RoleName = r.RoleName, Description = r.Description, IsSystemRole = r.IsSystemRole, IsActive = true };
                await roleRepo.AddAsync(target, ct);
                added++;
            }
            roleMap[r.RoleId] = target;
            summary.RolesByName[target.RoleName] = target;
        }
        await _uow.SaveChangesAsync(ct);
        var rpRepo = _uow.Repository<RolePermission>();
        var existingRp = (await rpRepo.ListAsync(null, ct)).Select(x => (x.RoleId, x.PermissionId)).ToHashSet();
        foreach (var (roleId, permissionId) in t.RolePermissions)
        {
            if (!roleMap.TryGetValue(roleId, out var role) || existingRp.Contains((role.RoleId, permissionId))) continue;
            await rpRepo.AddAsync(new RolePermission { RoleId = role.RoleId, PermissionId = permissionId }, ct);
        }
        await _uow.SaveChangesAsync(ct);
        summary.Lines.Add($"{added} roles with their permissions");
        return summary;
    }

    private async Task<int> CopyAsync<T>(IEnumerable<T> source, Func<T, string> code, Func<T, T> copy, CancellationToken ct) where T : class
    {
        var repo = _uow.Repository<T>();
        var existing = (await repo.ListAsync(null, ct)).Select(code).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var item in source)
        {
            if (existing.Contains(code(item))) continue;
            await repo.AddAsync(copy(item), ct);
            added++;
        }
        await _uow.SaveChangesAsync(ct);
        return added;
    }

    private async Task CreateStructureAsync(CompanySetupRequest request, CompanyDetailsForm d, CopySummary summary, CancellationToken ct)
    {
        var office = request.HeadOfficeName?.Trim();
        if (!string.IsNullOrEmpty(office) && !await _uow.Repository<Location>().AnyAsync(l => l.LocationCode == "HQ", ct))
        {
            await _uow.Repository<Location>().AddAsync(new Location { LocationCode = "HQ", LocationName = office, City = d.City,
                Country = d.Country, AddressLine = d.AddressLine, IsActive = true }, ct);
            summary.Lines.Add($"location \"{office}\" (HQ)");
        }
        var dept = request.FirstDepartmentName?.Trim();
        if (!string.IsNullOrEmpty(dept) && !await _uow.Repository<Department>().AnyAsync(x => x.DepartmentCode == "IT", ct))
        {
            await _uow.Repository<Department>().AddAsync(new Department { DepartmentCode = "IT", DepartmentName = dept, IsActive = true }, ct);
            summary.Lines.Add($"department \"{dept}\" (IT)");
        }
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Membership with the System Administrator role; a new account gets a one-time temporary password.</summary>
    private async Task<string?> CreateAdministratorAsync(int companyId, User? existing, CompanySetupRequest request, string username, CancellationToken ct)
    {
        string? temporary = null;
        var user = existing;
        if (user is null)
        {
            user = new User { Username = username, DisplayName = request.NewDisplayName!.Trim(), Email = request.NewEmail!.Trim(), IsActive = true };
            temporary = NewTemporaryPassword(user);
            _accounts.SetPassword(user, temporary, _clock.UtcNow, mustChange: true);
            await _uow.Repository<User>().AddAsync(user, ct);
        }
        var hasDefault = existing is not null && await _uow.Repository<CompanyUser>().AnyAsync(m => m.UserId == existing.UserId && m.IsDefault, ct);
        await _uow.Repository<CompanyUser>().AddAsync(new CompanyUser { CompanyId = companyId, User = user, IsActive = true, IsDefault = !hasDefault,
            CreatedBy = _currentUser.UserName, CreatedDate = _clock.UtcNow }, ct);
        await _uow.SaveChangesAsync(ct);
        var adminRole = await _uow.Repository<Role>().FirstOrDefaultAsync(r => r.RoleName == "SystemAdministrator", ct)
                        ?? throw new InvalidOperationException("the SystemAdministrator role was not copied");
        await _uow.Repository<UserRole>().AddAsync(new UserRole { UserId = user.UserId, RoleId = adminRole.RoleId }, ct);
        await _uow.SaveChangesAsync(ct);
        return temporary;
    }

    private string NewTemporaryPassword(User user)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = TemporaryPasswordGenerator.Generate(_options.TemporaryPasswordLength);
            if (_policy.Validate(candidate, user.Username, user.DisplayName).Count == 0) return candidate;
        }
        throw new InvalidOperationException("Could not generate a temporary password that satisfies the policy.");
    }

    // ------------------------------------------------------------------ validation and mapping

    private static CompanyDetailsForm Normalize(CompanyDetailsForm f)
    {
        static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        return new CompanyDetailsForm
        {
            CompanyCode = (f.CompanyCode ?? string.Empty).Trim().ToUpperInvariant(),
            LegalName = (f.LegalName ?? string.Empty).Trim(), DisplayName = (f.DisplayName ?? string.Empty).Trim(),
            RegistrationNo = T(f.RegistrationNo), Industry = T(f.Industry), Email = T(f.Email), Phone = T(f.Phone), Website = T(f.Website),
            Country = T(f.Country) ?? "Saudi Arabia", City = T(f.City), AddressLine = T(f.AddressLine),
            TimeZone = T(f.TimeZone) ?? "Arab Standard Time", LanguageCode = T(f.LanguageCode) ?? "en", BaseCurrencyId = f.BaseCurrencyId,
            AssetNumberPrefix = (f.AssetNumberPrefix ?? string.Empty).Trim().ToUpperInvariant(),
            HeaderColor = (f.HeaderColor ?? string.Empty).Trim(), PrimaryColor = (f.PrimaryColor ?? string.Empty).Trim(),
            AccentColor = (f.AccentColor ?? string.Empty).Trim(), LoginTagline = T(f.LoginTagline), RemoveLogo = f.RemoveLogo, RowVersion = f.RowVersion,
            NameArabic = T(f.NameArabic), ParentCompanyId = f.ParentCompanyId, CompanyType = T(f.CompanyType), VatNumber = T(f.VatNumber), Region = T(f.Region),
            DateFormat = T(f.DateFormat) ?? "dd MMM yyyy", NotificationsEnabled = f.NotificationsEnabled, AssetAuditEnabled = f.AssetAuditEnabled,
            DocNumberingMode = T(f.DocNumberingMode) ?? Application.Documents.NumberingModes.Continuous, FiscalYearStartMonth = f.FiscalYearStartMonth,
            DocYearFormat = T(f.DocYearFormat) ?? Application.Documents.YearFormats.Year, DocNumberDigits = f.DocNumberDigits,
            LogoUrl = T(f.LogoUrl)
        };
    }

    private static IEnumerable<Error> ValidateDetails(CompanyDetailsForm d, CompanyLogo? logo)
    {
        if (!CodePattern().IsMatch(d.CompanyCode)) yield return Error.Validation("Company code: 2-10 capital letters or digits (e.g. AMGH).", "Details.CompanyCode");
        if (d.LegalName.Length is < 2 or > 200) yield return Error.Validation("Enter the legal name (2-200 characters).", "Details.LegalName");
        if (d.DisplayName.Length is < 2 or > 100) yield return Error.Validation("Enter the display name (2-100 characters).", "Details.DisplayName");
        if (!PrefixPattern().IsMatch(d.AssetNumberPrefix)) yield return Error.Validation("Asset number prefix: 2-20 capital letters, digits or '-' (e.g. XYZ-IT).", "Details.AssetNumberPrefix");
        if (d.Email is not null && !EmailPattern().IsMatch(d.Email)) yield return Error.Validation("Enter a valid e-mail address.", "Details.Email");
        if (d.Phone?.Length > 30) yield return Error.Validation("Phone: up to 30 characters.", "Details.Phone");
        // the same limits as the database columns (a longer value would otherwise fail inside SQL Server)
        if (d.RegistrationNo?.Length > 50) yield return Error.Validation("Registration number: up to 50 characters.", "Details.RegistrationNo");
        if (d.Industry?.Length > 100) yield return Error.Validation("Industry: up to 100 characters.", "Details.Industry");
        if (d.Email?.Length > 256) yield return Error.Validation("E-mail: up to 256 characters.", "Details.Email");
        if (d.Website?.Length > 250) yield return Error.Validation("Website: up to 250 characters.", "Details.Website");
        if (d.Country.Length > 100) yield return Error.Validation("Country: up to 100 characters.", "Details.Country");
        if (d.City?.Length > 100) yield return Error.Validation("City: up to 100 characters.", "Details.City");
        if (d.AddressLine?.Length > 500) yield return Error.Validation("Address: up to 500 characters.", "Details.AddressLine");
        if (d.NameArabic?.Length > 200) yield return Error.Validation("Arabic name: up to 200 characters.", "Details.NameArabic");
        if (d.DocNumberingMode is not (Application.Documents.NumberingModes.Continuous or Application.Documents.NumberingModes.Yearly))
            yield return Error.Validation("Choose continuous or yearly document numbering.", "Details.DocNumberingMode");
        if (d.FiscalYearStartMonth is < 1 or > 12) yield return Error.Validation("Fiscal year start: a month (1-12).", "Details.FiscalYearStartMonth");
        if (d.DocYearFormat is not (Application.Documents.YearFormats.Year or Application.Documents.YearFormats.FiscalYear))
            yield return Error.Validation("Choose the year format.", "Details.DocYearFormat");
        if (d.DocNumberDigits is < 5 or > 8) yield return Error.Validation("Digits: 5 to 8.", "Details.DocNumberDigits");
        if (d.VatNumber is not null && !System.Text.RegularExpressions.Regex.IsMatch(d.VatNumber, "^[0-9A-Za-z-]{5,30}$"))
            yield return Error.Validation("VAT number: 5-30 letters or digits (Saudi VAT numbers have 15 digits).", "Details.VatNumber");
        if (d.Region?.Length > 100) yield return Error.Validation("Region: up to 100 characters.", "Details.Region");
        if (d.CompanyType is not null && !CompanyDetailsForm.CompanyTypes.Contains(d.CompanyType)) yield return Error.Validation("Choose a company type from the list.", "Details.CompanyType");
        if (!CompanyDetailsForm.DateFormats.Contains(d.DateFormat)) yield return Error.Validation("Choose a date format from the list.", "Details.DateFormat");
        if (d.Website is not null && !(Uri.TryCreate(d.Website, UriKind.Absolute, out var u) && u.Scheme is "https" or "http"))
            yield return Error.Validation("Website: a full address such as https://example.com.", "Details.Website");
        if (TimeZoneInfo.TryFindSystemTimeZoneById(d.TimeZone, out _) is false) yield return Error.Validation("Unknown time zone.", "Details.TimeZone");
        foreach (var (value, field) in new[] { (d.HeaderColor, "HeaderColor"), (d.PrimaryColor, "PrimaryColor"), (d.AccentColor, "AccentColor") })
            if (!ColorPattern().IsMatch(value)) yield return Error.Validation("Colours are written as #RRGGBB.", "Details." + field);
        if (d.LoginTagline?.Length > 150) yield return Error.Validation("Tagline: up to 150 characters.", "Details.LoginTagline");
        if (logo is not null && LogoProblem(logo) is { } problem) yield return Error.Validation(problem, "Logo");
    }

    /// <summary>PNG or JPEG only (checked by file signature, not by name), up to 512 KB. SVG is refused: it can carry script.</summary>
    internal static string? LogoProblem(CompanyLogo logo)
    {
        if (logo.Content.Length == 0) return "The logo file is empty.";
        if (logo.Content.Length > MaxLogoBytes) return "The logo must be 512 KB or smaller.";
        var png = logo.Content.Length > 8 && logo.Content.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var jpg = logo.Content.Length > 3 && logo.Content[0] == 0xFF && logo.Content[1] == 0xD8 && logo.Content[2] == 0xFF;
        return png || jpg ? null : "The logo must be a PNG or JPEG image.";
    }

    private static void Apply(Company c, CompanyDetailsForm d)
    {
        c.CompanyCode = d.CompanyCode; c.LegalName = d.LegalName; c.DisplayName = d.DisplayName; c.RegistrationNo = d.RegistrationNo;
        c.Industry = d.Industry; c.Email = d.Email; c.Phone = d.Phone; c.Website = d.Website; c.Country = d.Country; c.City = d.City;
        c.AddressLine = d.AddressLine; c.TimeZone = d.TimeZone; c.LanguageCode = d.LanguageCode; c.BaseCurrencyId = d.BaseCurrencyId;
        c.AssetNumberPrefix = d.AssetNumberPrefix;
        c.NameArabic = d.NameArabic; c.ParentCompanyId = d.ParentCompanyId; c.CompanyType = d.CompanyType; c.VatNumber = d.VatNumber;
        c.Region = d.Region; c.DateFormat = d.DateFormat; c.NotificationsEnabled = d.NotificationsEnabled; c.AssetAuditEnabled = d.AssetAuditEnabled;
        // document numbering (Release 2.13): changing it affects new documents only; issued numbers never change
        c.DocNumberingMode = d.DocNumberingMode; c.FiscalYearStartMonth = (byte)d.FiscalYearStartMonth;
        c.DocYearFormat = d.DocYearFormat; c.DocNumberDigits = (byte)d.DocNumberDigits;
    }

    private static void ApplyBranding(CompanyBranding b, CompanyDetailsForm d, CompanyLogo? logo)
    {
        b.HeaderColor = d.HeaderColor.ToUpperInvariant(); b.PrimaryColor = d.PrimaryColor.ToUpperInvariant();
        b.AccentColor = d.AccentColor.ToUpperInvariant(); b.LoginTagline = d.LoginTagline;
        if (logo is null) return;
        var png = logo.Content.Length > 1 && logo.Content[0] == 0x89;
        b.LogoImage = logo.Content;
        b.LogoSourceUrl = logo.SourceUrl;
        b.LogoContentType = png ? "image/png" : "image/jpeg";
    }
}
