using AMGH.ITInventory.Application.Abstractions.Mail;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Security.Platform;

/// <summary>A company's sign-in security settings. The single sign-on secret is never returned.</summary>
public sealed record CompanySecurity(int CompanyId, string CompanyCode, string DisplayName, byte MfaPolicy, string? AllowedIpRanges, bool SsoEnabled,
    string? SsoAuthority, string? SsoClientId, bool SsoSecretSet, string? SsoAllowedDomain);

public sealed record CompanySecurityUpdate(byte MfaPolicy, string? AllowedIpRanges, bool SsoEnabled, string? SsoAuthority, string? SsoClientId,
    string? SsoClientSecret, string? SsoAllowedDomain, string? CurrentIp);

public interface ICompanySecurityService
{
    Task<CompanySecurity?> GetAsync(int companyId, CancellationToken cancellationToken = default);

    Task<CompanySecurity?> GetByCodeAsync(string companyCode, CancellationToken cancellationToken = default);

    /// <summary>The unprotected single sign-on secret of the company, for the token exchange only.</summary>
    Task<string?> GetSsoSecretAsync(int companyId, CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(int companyId, CompanySecurityUpdate update, string by, CancellationToken cancellationToken = default);
}

internal sealed class CompanySecurityService : ICompanySecurityService
{
    private readonly IUnitOfWork _uow;
    private readonly ISecretProtector _secrets;

    public CompanySecurityService(IUnitOfWork uow, ISecretProtector secrets)
    {
        _uow = uow;
        _secrets = secrets;
    }

    private static CompanySecurity Map(Company c) => new(c.CompanyId, c.CompanyCode, c.DisplayName, c.MfaPolicy, c.AllowedIpRanges, c.SsoEnabled,
        c.SsoAuthority, c.SsoClientId, !string.IsNullOrEmpty(c.SsoClientSecret), c.SsoAllowedDomain);

    public async Task<CompanySecurity?> GetAsync(int companyId, CancellationToken cancellationToken = default) =>
        (await _uow.Repository<Company>().FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken)) is { } c ? Map(c) : null;

    public async Task<CompanySecurity?> GetByCodeAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var code = (companyCode ?? string.Empty).Trim().ToUpperInvariant();
        return (await _uow.Repository<Company>().FirstOrDefaultAsync(c => c.CompanyCode == code, cancellationToken)) is { } c ? Map(c) : null;
    }

    public async Task<string?> GetSsoSecretAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var c = await _uow.Repository<Company>().FirstOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        return string.IsNullOrEmpty(c?.SsoClientSecret) ? null : _secrets.Unprotect(c.SsoClientSecret);
    }

    public async Task<Result> SaveAsync(int companyId, CompanySecurityUpdate update, string by, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var company = await _uow.Repository<Company>().FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken);
        if (company is null) return Result.Failure(Error.NotFound("Company not found."));
        if (update.MfaPolicy > 2) return Result.Failure(Error.Validation("Choose one of the two-step verification options.", "mfa"));

        var ranges = string.IsNullOrWhiteSpace(update.AllowedIpRanges) ? null : string.Join(", ", update.AllowedIpRanges.Split(IpRules.Separators, StringSplitOptions.RemoveEmptyEntries));
        if (ranges is not null)
        {
            if (ranges.Length > IpRules.MaxLength) return Result.Failure(Error.Validation("The list of allowed addresses is too long.", "ip"));
            var (rules, bad) = IpRules.Parse(ranges);
            if (bad is not null) return Result.Failure(Error.Validation($"\"{bad}\" is not an IP address or range (examples: 203.0.113.7, 10.20.0.0/16).", "ip"));
            // a mistake here would lock everybody, the person saving included, out of the company
            if (rules.Count > 0 && !IpRules.Allows(update.CurrentIp, ranges))
                return Result.Failure(Error.Validation($"Your own address ({update.CurrentIp ?? "unknown"}) is not in the list. Add it first, or you would lock yourself out.", "ip"));
        }

        string? authority = string.IsNullOrWhiteSpace(update.SsoAuthority) ? null : update.SsoAuthority.Trim().TrimEnd('/');
        if (authority is not null && !(Uri.TryCreate(authority, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.Query)))
            return Result.Failure(Error.Validation("The sign-in provider address must be an https address (for example https://login.microsoftonline.com/<directory id>/v2.0).", "sso"));
        var clientId = string.IsNullOrWhiteSpace(update.SsoClientId) ? null : update.SsoClientId.Trim();
        var domain = string.IsNullOrWhiteSpace(update.SsoAllowedDomain) ? null : update.SsoAllowedDomain.Trim().TrimStart('@').ToLowerInvariant();
        var secretSet = !string.IsNullOrEmpty(company.SsoClientSecret) || !string.IsNullOrWhiteSpace(update.SsoClientSecret);
        if (update.SsoEnabled && (authority is null || clientId is null || !secretSet))
            return Result.Failure(Error.Validation("Single sign-on needs the provider address, the client id and the client secret.", "sso"));

        company.MfaPolicy = update.MfaPolicy;
        company.AllowedIpRanges = ranges;
        company.SsoEnabled = update.SsoEnabled;
        company.SsoAuthority = authority;
        company.SsoClientId = clientId;
        company.SsoAllowedDomain = domain;
        if (!string.IsNullOrWhiteSpace(update.SsoClientSecret)) company.SsoClientSecret = _secrets.Protect(update.SsoClientSecret.Trim());
        company.ModifiedBy = by;
        company.ModifiedDate = DateTime.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
