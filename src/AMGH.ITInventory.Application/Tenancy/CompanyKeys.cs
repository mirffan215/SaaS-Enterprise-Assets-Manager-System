using System.Security.Cryptography;
using System.Text;

namespace AMGH.ITInventory.Application.Tenancy;

/// <summary>
/// A company key: the one-time code the operator hands to a customer so they can create their own company. Looks like <c>EAMS-7K3Q-M9XD-4TWB</c>
/// (12 characters, no look-alike letters: 60 bits). Only its SHA-256 is stored, so a key can be shown once and never read back.
/// </summary>
public static class CompanyKeyFormat
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const string Prefix = "EAMS";

    public static string Generate()
    {
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        var s = new string(chars);
        return $"{Prefix}-{s[..4]}-{s[4..8]}-{s[8..]}";
    }

    /// <summary>Upper case, without dashes and spaces, the EAMS prefix removed: what people type is compared in this form.</summary>
    public static string Normalise(string? key)
    {
        var s = new string((key ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return s.StartsWith(Prefix, StringComparison.Ordinal) && s.Length == 16 ? s[Prefix.Length..] : s;
    }

    public static bool IsWellFormed(string? key)
    {
        var n = Normalise(key);
        return n.Length == 12 && n.All(c => Alphabet.Contains(c, StringComparison.Ordinal));
    }

    public static string Hash(string? key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("eams-company-key:" + Normalise(key))));

    /// <summary>The last four characters, kept so the operator can recognise a key in the list.</summary>
    public static string Hint(string key) => Normalise(key)[^4..];
}

public sealed record CompanyKeyInfo(int Id, string Hint, string? IssuedTo, string? Note, int? RequestId, DateTime CreatedAtUtc, string CreatedBy,
    DateTime ExpiresAtUtc, DateTime? UsedAtUtc, string? UsedCompanyCode, DateTime? RevokedAtUtc)
{
    public string Status(DateTime nowUtc) => RevokedAtUtc is not null ? "Revoked" : UsedAtUtc is not null ? "Used" : ExpiresAtUtc <= nowUtc ? "Expired" : "Unused";
}

public sealed record DemoRequestInfo(int Id, DateTime CreatedAtUtc, string FullName, string CompanyName, string Email, string? Phone, string? Message,
    string Status, int? KeyId, DateTime? HandledAtUtc);

public static class DemoRequestStatus
{
    public const string New = "New", KeySent = "KeySent", Declined = "Declined";
}

/// <summary>Company keys and the demo requests they answer (both live in the central catalog).</summary>
public interface ICompanyKeyStore
{
    /// <summary>Makes a new key valid for <paramref name="validDays"/> days. The plain key is returned here and never again.</summary>
    Task<(string Key, CompanyKeyInfo Info)> IssueAsync(string? issuedTo, string? note, int? requestId, int validDays, string createdBy, CancellationToken cancellationToken = default);

    /// <summary>True when the key exists, is unused, not revoked and not expired (does not use it up).</summary>
    Task<bool> IsValidAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uses the key up for this company code in one atomic step, so two people cannot share a key. Call <see cref="ReleaseAsync"/> when the company
    /// could not be created, so the customer can try again with the same key.
    /// </summary>
    Task<bool> TryReserveAsync(string key, string companyCode, CancellationToken cancellationToken = default);

    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyKeyInfo>> ListAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Withdraws an unused key.</summary>
    Task<bool> RevokeAsync(int keyId, CancellationToken cancellationToken = default);

    Task<int> AddRequestAsync(string fullName, string companyName, string email, string? phone, string? message, string? ipAddress, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DemoRequestInfo>> ListRequestsAsync(int take, CancellationToken cancellationToken = default);

    Task<DemoRequestInfo?> GetRequestAsync(int requestId, CancellationToken cancellationToken = default);

    Task SetRequestStatusAsync(int requestId, string status, int? keyId, CancellationToken cancellationToken = default);
}
