using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.Views;

public sealed record SavedViewItem(int Id, string Name, string QueryString, bool IsMine, bool IsShared, string CreatedBy);

/// <summary>
/// Saved list views (filters, sort, columns) per page, e.g. the asset register. Stored with the saved reports
/// (dbo.tblSavedReport, code "view:&lt;page&gt;"), so they share its company isolation and soft delete.
/// </summary>
public interface ISavedViewService
{
    Task<IReadOnlyList<SavedViewItem>> ListAsync(string page, CancellationToken cancellationToken = default);
    Task<Result<int>> SaveAsync(string page, string name, string queryString, bool shared, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

internal sealed class SavedViewService : ISavedViewService
{
    private static readonly HashSet<string> Pages = new(StringComparer.Ordinal) { "assets" };
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;

    public SavedViewService(IUnitOfWork uow, ICurrentUserService user)
    {
        _uow = uow;
        _user = user;
    }

    private static string Code(string page) => "view:" + page;

    public async Task<IReadOnlyList<SavedViewItem>> ListAsync(string page, CancellationToken cancellationToken = default)
    {
        var code = Code(page);
        var me = _user.UserId ?? -1;
        var rows = await _uow.Repository<SavedReport>().QueryAsync(q => q.Where(s => s.ReportCode == code && (s.OwnerUserId == me || s.IsShared))
            .OrderBy(s => s.Name), cancellationToken);
        return rows.Select(s => new SavedViewItem(s.SavedReportId, s.Name, s.QueryString, s.OwnerUserId == me, s.IsShared, s.CreatedBy)).ToList();
    }

    public async Task<Result<int>> SaveAsync(string page, string name, string queryString, bool shared, CancellationToken cancellationToken = default)
    {
        if (!Pages.Contains(page)) return Result<int>.Failure(Error.NotFound("Unknown list."));
        name = (name ?? string.Empty).Trim();
        if (name.Length is < 2 or > 100) return Result<int>.Failure(Error.Validation("Give the view a name (2-100 characters).", "name"));
        if (_user.UserId is not { } me) return Result<int>.Failure(Error.Forbidden("Sign in to save views."));
        // keep only the list's own parameters; never the page number
        var qs = string.Join("&", (queryString ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("Page=", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("__", StringComparison.Ordinal)));
        if (qs.Length > 2000) return Result<int>.Failure(Error.Validation("This view has too many filters to save."));
        var code = Code(page);
        var existing = await _uow.Repository<SavedReport>().FirstOrDefaultAsync(s => s.ReportCode == code && s.OwnerUserId == me && s.Name == name, cancellationToken);
        if (existing is not null)
        {
            existing.QueryString = qs;
            existing.IsShared = shared;
        }
        else
        {
            existing = new SavedReport { ReportCode = code, Name = name, QueryString = qs, IsShared = shared, OwnerUserId = me };
            await _uow.Repository<SavedReport>().AddAsync(existing, cancellationToken);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        return Result<int>.Success(existing.SavedReportId);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var me = _user.UserId ?? -1;
        var view = await _uow.Repository<SavedReport>().FirstOrDefaultAsync(s => s.SavedReportId == id && s.OwnerUserId == me && s.ReportCode.StartsWith("view:"), cancellationToken);
        if (view is null) return Result.Failure(Error.NotFound("View not found (only its owner can delete it)."));
        _uow.Repository<SavedReport>().Remove(view);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
