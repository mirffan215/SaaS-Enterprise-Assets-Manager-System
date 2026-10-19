using System.Security.Cryptography;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Abstractions.Storage;
using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.Assets;

public sealed record DocumentUpload(int AssetId, DocumentType DocumentType, string Title, string OriginalFileName,
    long Length, Stream Content, int? ClassificationId, string? Description);

public sealed record DocumentDownload(string FileName, string ContentType, Stream Content);

public interface IAssetDocumentService
{
    Task<IReadOnlyList<AssetDocumentItem>> ListAsync(int assetId, CancellationToken cancellationToken = default);
    Task<Result<int>> UploadAsync(DocumentUpload upload, CancellationToken cancellationToken = default);
    Task<DocumentDownload?> DownloadAsync(int assetId, int documentId, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(int assetId, int documentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Asset attachments (invoices, hand-over forms, certificates). Files are stored outside the web
/// root under a random name; type is decided from the extension AND the file's signature, never
/// from what the browser claims; a SHA-256 hash is kept for integrity checks.
/// </summary>
internal sealed class AssetDocumentService : IAssetDocumentService
{
    public const long MaxBytes = 25L * 1024 * 1024;

    private static readonly Dictionary<string, (string ContentType, byte[][] Signatures)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", new[] { "%PDF"u8.ToArray() }),
        [".png"] = ("image/png", new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } }),
        [".jpg"] = ("image/jpeg", new[] { new byte[] { 0xFF, 0xD8, 0xFF } }),
        [".jpeg"] = ("image/jpeg", new[] { new byte[] { 0xFF, 0xD8, 0xFF } }),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } }),
        [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } }),
        [".msg"] = ("application/vnd.ms-outlook", new[] { new byte[] { 0xD0, 0xCF, 0x11, 0xE0 } }),
        [".txt"] = ("text/plain", Array.Empty<byte[]>()),
        [".csv"] = ("text/csv", Array.Empty<byte[]>()),
    };

    public static IReadOnlyCollection<string> AllowedExtensions => Allowed.Keys;

    private readonly IUnitOfWork _uow;
    private readonly IFileStorage _storage;
    private readonly ICurrentUserService _user;
    private readonly IDateTimeProvider _clock;

    public AssetDocumentService(IUnitOfWork uow, IFileStorage storage, ICurrentUserService user, IDateTimeProvider clock)
    {
        _uow = uow;
        _storage = storage;
        _user = user;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AssetDocumentItem>> ListAsync(int assetId, CancellationToken cancellationToken = default) =>
        await _uow.Repository<AssetDocument>().QueryAsync(q => q.Where(d => d.AssetId == assetId)
            .OrderByDescending(d => d.AssetDocumentId)
            .Select(d => new AssetDocumentItem(d.AssetDocumentId, d.DocumentType.ToString(), d.DocumentTitle, d.OriginalFileName,
                d.FileSizeBytes, d.Classification == null ? null : d.Classification.ClassificationName, d.CreatedBy, d.CreatedDate)),
            cancellationToken);

    public async Task<Result<int>> UploadAsync(DocumentUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var title = (upload.Title ?? string.Empty).Trim();
        var originalName = Path.GetFileName(upload.OriginalFileName ?? string.Empty).Trim();
        var extension = Path.GetExtension(originalName);

        if (!await _uow.Repository<Asset>().AnyAsync(a => a.AssetId == upload.AssetId, cancellationToken))
            return Result<int>.Failure(Error.NotFound("This asset no longer exists."));
        if (title.Length is 0 or > 200) return Result<int>.Failure(Error.Validation("Enter a title of up to 200 characters.", "Title"));
        if (originalName.Length is 0 or > 260) return Result<int>.Failure(Error.Validation("The file name is missing or too long.", "File"));
        if (upload.Length <= 0) return Result<int>.Failure(Error.Validation("The file is empty.", "File"));
        if (upload.Length > MaxBytes) return Result<int>.Failure(Error.Validation("Files can be up to 25 MB.", "File"));
        if (!Allowed.TryGetValue(extension, out var type))
            return Result<int>.Failure(Error.Validation($"This file type is not allowed. Allowed: {string.Join(", ", Allowed.Keys)}.", "File"));
        if ((upload.Description?.Length ?? 0) > 500) return Result<int>.Failure(Error.Validation("Description must be 500 characters or fewer.", "Description"));

        // Buffer once: signature check, hash, then store.
        await using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxBytes) return Result<int>.Failure(Error.Validation("Files can be up to 25 MB.", "File"));
        var bytes = buffer.ToArray();
        if (!SignatureMatches(bytes, type.Signatures, isText: type.Signatures.Length == 0))
            return Result<int>.Failure(Error.Validation("The file content does not match its extension.", "File"));

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        buffer.Position = 0;
        await _storage.SaveAsync(StorageAreas.AssetDocuments, storedName, buffer, cancellationToken);

        var document = new AssetDocument
        {
            AssetId = upload.AssetId,
            DocumentType = upload.DocumentType,
            DocumentTitle = title,
            OriginalFileName = originalName,
            StoredFileName = storedName,
            ContentType = type.ContentType,
            FileSizeBytes = buffer.Length,
            Sha256Hash = hash,
            ClassificationId = upload.ClassificationId,
            Description = string.IsNullOrWhiteSpace(upload.Description) ? null : upload.Description.Trim()
        };
        await _uow.Repository<AssetDocument>().AddAsync(document, cancellationToken);
        await _uow.Repository<AssetHistory>().AddAsync(new AssetHistory
        {
            AssetId = upload.AssetId,
            EventType = AssetEventType.DocumentAttached,
            NewValue = Truncate($"{DisplayText.Humanize(upload.DocumentType.ToString())}: {title}", 1000),
            Remarks = Truncate(originalName, 1000),
            PerformedBy = _user.UserName,
            EventDate = _clock.UtcNow
        }, cancellationToken);
        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _storage.DeleteAsync(StorageAreas.AssetDocuments, storedName, CancellationToken.None);
            throw;
        }
        return Result<int>.Success(document.AssetDocumentId);
    }

    public async Task<DocumentDownload?> DownloadAsync(int assetId, int documentId, CancellationToken cancellationToken = default)
    {
        var docs = await _uow.Repository<AssetDocument>().QueryAsync(q => q
            .Where(d => d.AssetDocumentId == documentId && d.AssetId == assetId), cancellationToken);
        if (docs.Count == 0) return null;
        var d = docs[0];
        var stream = await _storage.OpenReadAsync(StorageAreas.AssetDocuments, d.StoredFileName, cancellationToken);
        return stream is null ? null : new DocumentDownload(d.OriginalFileName, d.ContentType, stream);
    }

    public async Task<Result> DeleteAsync(int assetId, int documentId, CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<AssetDocument>();
        var doc = await repo.FirstOrDefaultAsync(d => d.AssetDocumentId == documentId && d.AssetId == assetId, cancellationToken);
        if (doc is null) return Result.Failure(Error.NotFound("The document no longer exists."));
        repo.Remove(doc); // soft delete: the file is kept for the audit trail
        await _uow.Repository<AssetHistory>().AddAsync(new AssetHistory
        {
            AssetId = assetId,
            EventType = AssetEventType.Updated,
            FieldName = "Document",
            OldValue = Truncate(doc.DocumentTitle, 1000),
            Remarks = "Attachment removed",
            PerformedBy = _user.UserName,
            EventDate = _clock.UtcNow
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    internal static bool SignatureMatches(ReadOnlySpan<byte> content, byte[][] signatures, bool isText)
    {
        if (isText)
        {
            var probe = content[..Math.Min(content.Length, 8192)];
            return probe.IndexOf((byte)0) < 0; // binary content disguised as text
        }
        foreach (var s in signatures)
            if (content.Length >= s.Length && content[..s.Length].SequenceEqual(s)) return true;
        return false;
    }

    private static string? Truncate(string? v, int max) => v is null ? null : v.Length <= max ? v : v[..max];
}
