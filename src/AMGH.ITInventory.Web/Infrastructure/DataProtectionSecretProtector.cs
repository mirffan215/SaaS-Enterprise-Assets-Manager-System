using System.Security.Cryptography;
using AMGH.ITInventory.Application.Abstractions.Mail;
using Microsoft.AspNetCore.DataProtection;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>
/// Encrypts the mail password / OAuth client secret with ASP.NET Data Protection (AES-256-CBC + HMACSHA256; the key ring is
/// protected by Windows DPAPI on the server). The database only ever holds the encrypted form.
/// </summary>
internal sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider) => _protector = provider.CreateProtector("AMGH.ITInventory.MailSecret.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string? Unprotect(string protectedSecret)
    {
        try { return _protector.Unprotect(protectedSecret); }
        catch (CryptographicException) { return null; } // key ring replaced: the secret must be entered again
    }
}
