using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;

namespace AMGH.ITInventory.Security.Platform;

/// <summary>
/// Public information about company workspaces: the picker (names and logos of active companies), sign-in
/// branding, labels. Never returns anything confidential and never company-owned data.
/// </summary>
public interface ICompanyDirectory
{
    Task<IReadOnlyList<WorkspaceCard>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<WorkspaceBranding?> GetBrandingAsync(string companyCode, CancellationToken cancellationToken = default);
    Task<WorkspaceBranding?> GetBrandingAsync(int companyId, CancellationToken cancellationToken = default);
    Task<CompanyLogo?> GetLogoAsync(string companyCode, CancellationToken cancellationToken = default);
}

internal sealed class CompanyDirectory : ICompanyDirectory
{
    private readonly IUnitOfWork _uow;

    public CompanyDirectory(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<WorkspaceCard>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await _uow.Repository<Company>().QueryAsync(q => q.Where(c => c.Status == CompanyStatuses.Active)
            .OrderBy(c => c.DisplayName)
            .Select(c => new WorkspaceCard(c.CompanyCode, c.DisplayName, c.Branding != null && c.Branding.LogoImage != null)), cancellationToken);

    public Task<WorkspaceBranding?> GetBrandingAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var code = (companyCode ?? string.Empty).Trim().ToUpperInvariant();
        return BrandingAsync(c => c.CompanyCode == code, cancellationToken);
    }

    public Task<WorkspaceBranding?> GetBrandingAsync(int companyId, CancellationToken cancellationToken = default) =>
        BrandingAsync(c => c.CompanyId == companyId, cancellationToken);

    private async Task<WorkspaceBranding?> BrandingAsync(System.Linq.Expressions.Expression<Func<Company, bool>> which, CancellationToken ct)
    {
        var rows = await _uow.Repository<Company>().QueryAsync(q => q.Where(which).Where(c => c.Status == CompanyStatuses.Active)
            .Select(c => new WorkspaceBranding(c.CompanyId, c.CompanyCode, c.DisplayName, c.LegalName, c.Phone,
                c.Branding == null ? BrandingDefaults.Header : c.Branding.HeaderColor,
                c.Branding == null ? BrandingDefaults.Primary : c.Branding.PrimaryColor,
                c.Branding == null ? BrandingDefaults.Accent : c.Branding.AccentColor,
                c.Branding == null ? null : c.Branding.LoginTagline,
                c.Branding != null && c.Branding.LogoImage != null, c.NameArabic, c.Email)), ct);
        return rows.Count == 1 ? rows[0] : null;
    }

    public async Task<CompanyLogo?> GetLogoAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        var code = (companyCode ?? string.Empty).Trim().ToUpperInvariant();
        var rows = await _uow.Repository<CompanyBranding>().QueryAsync(q => q
            .Where(b => b.Company.CompanyCode == code && b.Company.Status == CompanyStatuses.Active && b.LogoImage != null)
            .Select(b => new CompanyLogo(b.LogoImage!, b.LogoContentType ?? "image/png", null)), cancellationToken);
        return rows.Count == 1 ? rows[0] : null;
    }
}
