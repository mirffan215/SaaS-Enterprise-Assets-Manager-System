using System.Security.Cryptography;
using System.Text;
using AMGH.ITInventory.Application.Abstractions.Lifecycle;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Abstractions.QrCodes;
using AMGH.ITInventory.Domain.Constants;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Errors;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.Labels;

public sealed record LabelAsset(int AssetId, string AssetNumber, string AssetName, string CategoryName, string StatusName,
    string? SerialNumber, string? Hostname, string? AssignedTo, string? Department, string? Location);

/// <summary>One label: QR design carries <see cref="QrSvg"/>; asset-tag design carries <see cref="BarcodeSvg"/>.</summary>
public sealed record LabelItem(LabelAsset Asset, string? QrSvg, string? BarcodeSvg, int Copies);

public sealed record LabelSheet(LabelDesign Design, LabelSize Size, Guid BatchId, IReadOnlyList<LabelItem> Labels);

/// <summary><see cref="PreviouslyPrinted"/> holds, per design, the assets that already had that design printed.</summary>
public sealed record LabelPreview(IReadOnlyList<LabelAsset> Assets, IReadOnlyDictionary<LabelDesign, IReadOnlySet<int>> PreviouslyPrinted,
    IReadOnlyList<string> Problems)
{
    public bool WasPrinted(LabelDesign design, int assetId) => PreviouslyPrinted.TryGetValue(design, out var s) && s.Contains(assetId);
}

public sealed record LabelPrintRequest(IReadOnlyList<int> AssetIds, LabelSize Size, int Copies, string? ReprintReason,
    LabelDesign Design = LabelDesign.QrLabel);

public sealed record LabelHistoryEntry(DateTime PrintedUtc, string Design, string LabelSize, int Copies, int QrVersion, bool IsReprint,
    string? ReprintReason, string PrintedBy);

public interface ILabelService
{
    const int MaxAssetsPerBatch = 200;

    Task<LabelPreview> PreviewAsync(IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates (or reuses) each asset's QR code, records the print in the label history and returns the
    /// sheet to print. Re-printing an already labelled asset needs <paramref name="canReprint"/> and a reason.
    /// </summary>
    Task<Result<LabelSheet>> PrintAsync(LabelPrintRequest request, string baseUrl, bool canReprint, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LabelHistoryEntry>> GetHistoryAsync(int assetId, CancellationToken cancellationToken = default);

    /// <summary>QR text. <paramref name="compact"/> (50×30 labels) keeps number, link, name and serial so the code stays printable.</summary>
    string BuildPayload(LabelAsset asset, string baseUrl, bool compact);
}

internal sealed class LabelService : ILabelService
{
    private const int MaxFieldLength = 40;

    private readonly IUnitOfWork _uow;
    private readonly IAssetLifecycleProcedures _procedures;
    private readonly IQrCodeGenerator _qr;
    private readonly IBarcodeGenerator _barcode;

    public LabelService(IUnitOfWork uow, IAssetLifecycleProcedures procedures, IQrCodeGenerator qr, IBarcodeGenerator barcode)
    {
        _uow = uow;
        _procedures = procedures;
        _qr = qr;
        _barcode = barcode;
    }

    public async Task<LabelPreview> PreviewAsync(IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        var ids = assetIds.Distinct().Take(ILabelService.MaxAssetsPerBatch).ToList();
        var assets = await LoadAsync(ids, cancellationToken);
        var printedRows = await _uow.Repository<QrLabelPrintLog>().QueryAsync(q => q.Where(p => ids.Contains(p.AssetId))
            .Select(p => new { p.LabelDesign, p.AssetId }).Distinct(), cancellationToken);
        var printed = Enum.GetValues<LabelDesign>().ToDictionary(d => d,
            d => (IReadOnlySet<int>)printedRows.Where(r => r.LabelDesign == d).Select(r => r.AssetId).ToHashSet());

        var problems = new List<string>();
        if (assetIds.Distinct().Count() > ILabelService.MaxAssetsPerBatch)
            problems.Add($"Only the first {ILabelService.MaxAssetsPerBatch} assets are included; print the rest in another batch.");
        var missing = ids.Count - assets.Count(a => a.Item2 != AssetStatusCodes.Disposed);
        if (assets.Any(a => a.Item2 == AssetStatusCodes.Disposed)) problems.Add("Disposed assets are left out: they no longer carry a label.");
        if (missing > assets.Count(a => a.Item2 == AssetStatusCodes.Disposed)) problems.Add("Some selected assets no longer exist.");

        return new LabelPreview(assets.Where(a => a.Item2 != AssetStatusCodes.Disposed).Select(a => a.Item1).ToList(), printed, problems);
    }

    public async Task<Result<LabelSheet>> PrintAsync(LabelPrintRequest request, string baseUrl, bool canReprint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        var preview = await PreviewAsync(request.AssetIds, cancellationToken);
        if (preview.Assets.Count == 0) return Result<LabelSheet>.Failure(Error.Validation("Select at least one asset that can be labelled."));
        if (request.Copies is < 1 or > 10) return Result<LabelSheet>.Failure(Error.Validation("Copies must be between 1 and 10.", "Copies"));
        if (!Enum.IsDefined(request.Size)) return Result<LabelSheet>.Failure(Error.Validation("Choose a label size.", "Size"));

        var reason = string.IsNullOrWhiteSpace(request.ReprintReason) ? null : request.ReprintReason.Trim();
        if (!Enum.IsDefined(request.Design)) return Result<LabelSheet>.Failure(Error.Validation("Choose a label design.", "Design"));
        var anyReprint = preview.Assets.Any(a => preview.WasPrinted(request.Design, a.AssetId));
        var what = request.Design == LabelDesign.AssetTag ? "an asset tag" : "a QR label";
        if (anyReprint && !canReprint)
            return Result<LabelSheet>.Failure(Error.Forbidden($"Some of these assets already have {what}. Reprinting needs the 'Reprint QR labels' permission."));
        if (anyReprint && (reason is null || reason.Length < 5))
            return Result<LabelSheet>.Failure(Error.Validation("Give the reason for reprinting (e.g. label damaged, details changed).", "ReprintReason"));
        if (reason is { Length: > 500 }) return Result<LabelSheet>.Failure(Error.Validation("The reason can be up to 500 characters.", "ReprintReason"));

        var batch = Guid.NewGuid();
        var compact = request.Size == LabelSize.Size50x30;
        try
        {
            var labels = await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var items = new List<LabelItem>();
                foreach (var asset in preview.Assets)
                {
                    var full = BuildPayload(asset, baseUrl, compact: false);
                    var qrId = await CurrentOrNewQrAsync(asset.AssetId, full, ct);
                    var isReprint = preview.WasPrinted(request.Design, asset.AssetId);
                    await _procedures.LogLabelPrintAsync(new LogLabelPrintRequest(qrId, request.Size, (short)request.Copies, batch,
                        isReprint, isReprint ? reason : null, request.Design), ct);
                    if (request.Design == LabelDesign.AssetTag)
                    {
                        items.Add(new LabelItem(asset, null, _barcode.Generate(asset.AssetNumber).Svg, request.Copies));
                    }
                    else
                    {
                        var printed = compact ? BuildPayload(asset, baseUrl, compact: true) : full;
                        items.Add(new LabelItem(asset, _qr.Generate(printed, quietZoneModules: 2).Svg, null, request.Copies));
                    }
                }
                return items;
            }, cancellationToken);
            return Result<LabelSheet>.Success(new LabelSheet(request.Design, request.Size, batch, labels));
        }
        catch (BusinessRuleException ex)
        {
            return Result<LabelSheet>.Failure(Error.BusinessRule(ex.Message));
        }
    }

    /// <summary>Keeps the stored QR version while the encoded details are unchanged; otherwise stores a new version.</summary>
    private async Task<int> CurrentOrNewQrAsync(int assetId, string payload, CancellationToken ct)
    {
        var hash = Sha256(payload);
        var current = await _uow.Repository<AssetQrCode>().QueryAsync(q => q.Where(c => c.AssetId == assetId && c.IsCurrent)
            .Select(c => new { c.AssetQRCodeId, c.PayloadHash }), ct);
        if (current.Count == 1 && string.Equals(current[0].PayloadHash, hash, StringComparison.OrdinalIgnoreCase))
            return current[0].AssetQRCodeId;
        var image = _qr.Generate(payload, quietZoneModules: 4);
        return await _procedures.SaveQrCodeAsync(new SaveQrCodeRequest(assetId, payload, hash, image.Png), ct);
    }

    public async Task<IReadOnlyList<LabelHistoryEntry>> GetHistoryAsync(int assetId, CancellationToken cancellationToken = default) =>
        await _uow.Repository<QrLabelPrintLog>().QueryAsync(q => q.Where(p => p.AssetId == assetId)
            .OrderByDescending(p => p.QrLabelPrintLogId).Take(200)
            .Select(p => new LabelHistoryEntry(p.PrintedDate, p.LabelDesign.ToString(), p.LabelSize.ToString(), p.Copies, p.AssetQrCode.QrVersion, p.IsReprint,
                p.ReprintReason, p.PrintedBy)), cancellationToken);

    public string BuildPayload(LabelAsset asset, string baseUrl, bool compact)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var sb = new StringBuilder();
        sb.Append(asset.AssetNumber).Append('\n');
        sb.Append(baseUrl.TrimEnd('/')).Append("/a/").Append(asset.AssetNumber).Append('\n');
        Line(sb, "Name", asset.AssetName);
        Line(sb, "SN", asset.SerialNumber);
        if (!compact)
        {
            Line(sb, "User", asset.AssignedTo);
            Line(sb, "Dept", asset.Department);
            Line(sb, "Loc", asset.Location);
            Line(sb, "Host", asset.Hostname);
            Line(sb, "Status", asset.StatusName);
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static void Line(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var v = value.Trim();
        if (v.Length > MaxFieldLength) v = v[..(MaxFieldLength - 1)] + "…";
        sb.Append(label).Append(": ").Append(v).Append('\n');
    }

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private async Task<List<(LabelAsset, string)>> LoadAsync(List<int> ids, CancellationToken ct)
    {
        var rows = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => ids.Contains(a.AssetId)).Select(a => new
        {
            a.AssetId, a.AssetNumber, a.AssetName, Category = a.AssetCategory.CategoryName, a.AssetStatus.StatusCode, a.AssetStatus.StatusName,
            a.SerialNumber, a.Hostname,
            AssignedTo = a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName + " (" + a.AssignedEmployee.EmployeeNumber + ")",
            Department = a.Department == null ? null : a.Department.DepartmentName,
            Location = a.Location == null ? null : a.Location.LocationName,
            Building = a.Building == null ? null : a.Building.BuildingName,
            Room = a.Room == null ? null : a.Room.RoomName
        }), ct);
        var order = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return rows.OrderBy(r => order[r.AssetId]).Select(r => (new LabelAsset(r.AssetId, r.AssetNumber, r.AssetName, r.Category, r.StatusName,
            r.SerialNumber, r.Hostname, r.AssignedTo, r.Department,
            string.Join(" / ", new[] { r.Location, r.Building, r.Room }.Where(x => x is not null)) is { Length: > 0 } loc ? loc : null), r.StatusCode)).ToList();
    }
}
