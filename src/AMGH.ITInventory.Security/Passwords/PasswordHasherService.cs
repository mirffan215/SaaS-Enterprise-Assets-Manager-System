using AMGH.ITInventory.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace AMGH.ITInventory.Security.Passwords;

public enum PasswordCheck
{
    Failed,
    Success,
    SuccessRehashNeeded
}

public interface IPasswordHasherService
{
    string Hash(User user, string password);
    PasswordCheck Verify(User user, string password);
    PasswordCheck Verify(User user, string hashedPassword, string password);

    /// <summary>Performs a dummy verification so unknown usernames take as long as wrong passwords.</summary>
    void SimulateVerification(string password);
}

/// <summary>
/// ASP.NET Core Identity V3 hashing: PBKDF2-HMAC-SHA512, 100,000 iterations, 128-bit salt.
/// Identical format to deployment\PowerShell\PasswordFunctions.ps1.
/// </summary>
internal sealed class PasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<User> _hasher = new();
    private readonly string _dummyHash;

    public PasswordHasherService() => _dummyHash = _hasher.HashPassword(new User(), Guid.NewGuid().ToString("N"));

    public string Hash(User user, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(user, password);
    }

    public PasswordCheck Verify(User user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Verify(user, user.PasswordHash, password);
    }

    public PasswordCheck Verify(User user, string hashedPassword, string password)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword)) return PasswordCheck.Failed;
        try
        {
            return _hasher.VerifyHashedPassword(user, hashedPassword, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed
            };
        }
        catch (FormatException)
        {
            return PasswordCheck.Failed; // corrupted or non-V3 hash
        }
    }

    public void SimulateVerification(string password) =>
        _hasher.VerifyHashedPassword(new User(), _dummyHash, string.IsNullOrEmpty(password) ? "x" : password);
}
