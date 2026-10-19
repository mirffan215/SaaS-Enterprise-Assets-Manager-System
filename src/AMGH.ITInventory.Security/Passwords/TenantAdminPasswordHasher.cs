using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Security.Passwords;

/// <summary>Applies the password policy and hashes the first administrator's password for a new tenant database.</summary>
internal sealed class TenantAdminPasswordHasher : ITenantAdminPasswordHasher
{
    private readonly IPasswordPolicy _policy;
    private readonly IPasswordHasherService _hasher;

    public TenantAdminPasswordHasher(IPasswordPolicy policy, IPasswordHasherService hasher)
    {
        _policy = policy;
        _hasher = hasher;
    }

    public Result<string> HashNew(string username, string email, string password)
    {
        var problems = _policy.Validate(password, username, email);
        if (problems.Count > 0)
            return Result<string>.Failure(problems.Select(p => Error.Validation(p, "tenant.password")).ToArray());
        return Result<string>.Success(_hasher.Hash(new User { Username = username }, password));
    }
}
