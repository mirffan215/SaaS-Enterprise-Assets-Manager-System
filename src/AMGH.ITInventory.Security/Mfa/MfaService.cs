using System.Security.Cryptography;
using System.Text;
using AMGH.ITInventory.Application.Abstractions.Mail;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Security.Mfa;

public sealed record MfaState(bool Enabled, bool SetupStarted, int RecoveryCodesLeft);

public sealed record MfaEnrollment(string Secret, string Uri);

public sealed record MfaUserRow(int UserId, string Username, string DisplayName, bool Enabled);

public enum MfaResult { Ok, Wrong, Locked }

public sealed record MfaVerification(MfaResult Result, DateTime? LockedUntilUtc = null, bool UsedRecoveryCode = false);

/// <summary>Two-step verification with an authenticator app, plus single-use recovery codes.</summary>
public interface IMfaService
{
    Task<MfaState> GetStateAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new secret for the person (replacing an unfinished setup); null when two-step verification is already on.</summary>
    Task<MfaEnrollment?> BeginEnrollmentAsync(int userId, string account, string issuer, CancellationToken cancellationToken = default);

    /// <summary>Turns two-step verification on when the code from the app is right; returns the recovery codes (shown once), or null when the code is wrong.</summary>
    Task<IReadOnlyList<string>?> ConfirmEnrollmentAsync(int userId, int companyId, string code, string? ip, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>Checks a code from the app or a recovery code. Wrong codes count towards the same lockout as wrong passwords.</summary>
    Task<MfaVerification> VerifyAsync(int userId, int companyId, string code, string? ip, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>The company's people and whether two-step verification is on for them.</summary>
    Task<IReadOnlyList<MfaUserRow>> ListAsync(int companyId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the recovery codes with new ones (shown once).</summary>
    Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Turns two-step verification off for the person (lost phone, left company) and ends their other sessions.</summary>
    Task ResetAsync(int userId, int companyId, string resetBy, string? ip, string? userAgent, CancellationToken cancellationToken = default);
}

internal sealed class MfaService : IMfaService
{
    private const int RecoveryCodeCount = 10;
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly IUnitOfWork _uow;
    private readonly ISecretProtector _secrets;
    private readonly IDateTimeProvider _clock;
    private readonly SecurityOptions _options;
    private readonly IAccountService _accounts;

    public MfaService(IUnitOfWork uow, ISecretProtector secrets, IDateTimeProvider clock, IOptions<SecurityOptions> options, IAccountService accounts)
    {
        _uow = uow;
        _secrets = secrets;
        _clock = clock;
        _options = options.Value;
        _accounts = accounts;
    }

    private Task<User?> UserAsync(int userId, CancellationToken ct) => _uow.Repository<User>().FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive, ct);

    public async Task<MfaState> GetStateAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await UserAsync(userId, cancellationToken);
        if (user is null) return new MfaState(false, false, 0);
        var left = user.HasTwoStep ? await _uow.Repository<UserRecoveryCode>().CountAsync(c => c.UserId == userId && c.UsedDate == null, cancellationToken) : 0;
        return new MfaState(user.HasTwoStep, user.TotpSecret is not null && !user.HasTwoStep, left);
    }

    public async Task<MfaEnrollment?> BeginEnrollmentAsync(int userId, string account, string issuer, CancellationToken cancellationToken = default)
    {
        var user = await UserAsync(userId, cancellationToken);
        if (user is null || user.HasTwoStep) return null;
        var secret = Totp.ToBase32(Totp.NewSecret());
        user.TotpSecret = _secrets.Protect(secret);
        user.TotpEnabledDate = null;
        user.TotpLastStep = null;
        await _uow.SaveChangesAsync(cancellationToken);
        return new MfaEnrollment(secret, Totp.Uri(issuer, account, secret));
    }

    public async Task<IReadOnlyList<string>?> ConfirmEnrollmentAsync(int userId, int companyId, string code, string? ip, string? userAgent, CancellationToken cancellationToken = default)
    {
        var user = await UserAsync(userId, cancellationToken);
        if (user is null || user.HasTwoStep || user.TotpSecret is null) return null;
        var secret = SecretBytes(user);
        if (secret is null || Totp.Verify(secret, code, _clock.UtcNow, null) is not { } step) return null;
        user.TotpEnabledDate = _clock.UtcNow;
        user.TotpLastStep = step;
        user.SecurityStamp = Guid.NewGuid(); // other sessions must sign in again, now with the second step
        var codes = await ReplaceRecoveryCodesAsync(user, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        await _accounts.LogEventAsync(user.UserId, companyId, user.Username, LoginEventType.MfaEnabled, ip, userAgent, null, cancellationToken);
        return codes;
    }

    public async Task<MfaVerification> VerifyAsync(int userId, int companyId, string code, string? ip, string? userAgent, CancellationToken cancellationToken = default)
    {
        var user = await UserAsync(userId, cancellationToken);
        if (user is null || !user.HasTwoStep) return new MfaVerification(MfaResult.Wrong);
        var now = _clock.UtcNow;
        if (user.LockoutEndUtc is { } until && until > now) return new MfaVerification(MfaResult.Locked, until);

        var secret = SecretBytes(user);
        if (secret is not null && Totp.Verify(secret, code, now, user.TotpLastStep) is { } step)
        {
            user.TotpLastStep = step;
            user.AccessFailedCount = 0;
            await _uow.SaveChangesAsync(cancellationToken);
            return new MfaVerification(MfaResult.Ok);
        }

        var normalised = NormaliseRecovery(code);
        if (normalised.Length == 10)
        {
            var hash = Hash(normalised);
            var recovery = await _uow.Repository<UserRecoveryCode>().FirstOrDefaultAsync(c => c.UserId == userId && c.CodeHash == hash && c.UsedDate == null, cancellationToken);
            if (recovery is not null)
            {
                recovery.UsedDate = now;
                user.AccessFailedCount = 0;
                await _uow.SaveChangesAsync(cancellationToken);
                await _accounts.LogEventAsync(user.UserId, companyId, user.Username, LoginEventType.RecoveryCodeUsed, ip, userAgent, null, cancellationToken);
                return new MfaVerification(MfaResult.Ok, UsedRecoveryCode: true);
            }
        }

        user.AccessFailedCount++;
        if (user.AccessFailedCount >= _options.MaxFailedAccessAttempts)
        {
            user.LockoutEndUtc = now.AddMinutes(_options.LockoutMinutes);
            user.AccessFailedCount = 0;
            await _uow.SaveChangesAsync(cancellationToken);
            await _accounts.LogEventAsync(user.UserId, companyId, user.Username, LoginEventType.Lockout, ip, userAgent, "Locked after wrong two-step codes", cancellationToken);
            return new MfaVerification(MfaResult.Locked, user.LockoutEndUtc);
        }
        await _uow.SaveChangesAsync(cancellationToken);
        await _accounts.LogEventAsync(user.UserId, companyId, user.Username, LoginEventType.LoginFailed, ip, userAgent, "Wrong two-step code", cancellationToken);
        return new MfaVerification(MfaResult.Wrong);
    }

    public async Task<IReadOnlyList<MfaUserRow>> ListAsync(int companyId, CancellationToken cancellationToken = default) =>
        (await _uow.Repository<CompanyUser>().QueryAsync(q => q.Where(m => m.CompanyId == companyId && m.IsActive)
            .OrderBy(m => m.User.Username)
            .Select(m => new MfaUserRow(m.User.UserId, m.User.Username, m.User.DisplayName, m.User.TotpSecret != null && m.User.TotpEnabledDate != null)), cancellationToken));

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await UserAsync(userId, cancellationToken) ?? throw new InvalidOperationException("User not found.");
        var codes = await ReplaceRecoveryCodesAsync(user, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return codes;
    }

    public async Task ResetAsync(int userId, int companyId, string resetBy, string? ip, string? userAgent, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<User>().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null) return;
        user.TotpSecret = null;
        user.TotpEnabledDate = null;
        user.TotpLastStep = null;
        user.SecurityStamp = Guid.NewGuid();
        foreach (var c in await _uow.Repository<UserRecoveryCode>().ListAsync(c => c.UserId == userId, cancellationToken)) _uow.Repository<UserRecoveryCode>().Remove(c);
        await _uow.SaveChangesAsync(cancellationToken);
        await _accounts.LogEventAsync(user.UserId, companyId, user.Username, LoginEventType.MfaReset, ip, userAgent, "Reset by " + resetBy, cancellationToken);
    }

    private byte[]? SecretBytes(User user) => user.TotpSecret is null ? null : _secrets.Unprotect(user.TotpSecret) is { } text ? Totp.FromBase32(text) : null;

    private async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(User user, CancellationToken ct)
    {
        var repo = _uow.Repository<UserRecoveryCode>();
        foreach (var old in await repo.ListAsync(c => c.UserId == user.UserId, ct)) repo.Remove(old);
        var codes = new List<string>();
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var raw = new string(Enumerable.Range(0, 10).Select(_ => RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)]).ToArray());
            codes.Add(raw[..5] + "-" + raw[5..]);
            await repo.AddAsync(new UserRecoveryCode { UserId = user.UserId, CodeHash = Hash(raw), CreatedDate = _clock.UtcNow }, ct);
        }
        return codes;
    }

    internal static string NormaliseRecovery(string? code) => new((code ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Hash(string normalised) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
}
