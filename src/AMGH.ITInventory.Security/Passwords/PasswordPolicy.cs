namespace AMGH.ITInventory.Security.Passwords;

public interface IPasswordPolicy
{
    /// <summary>Returns the list of unmet requirements; empty means the password is acceptable.</summary>
    IReadOnlyList<string> Validate(string? password, string? username = null, string? displayName = null);
}

/// <summary>
/// Access Control Procedure password rules: minimum length, four character classes, no
/// username / name fragments, and not a well-known or keyboard-pattern password.
/// </summary>
internal sealed class PasswordPolicy : IPasswordPolicy
{
    private static readonly string[] ForbiddenFragments =
    {
        "password", "passw0rd", "p@ssw0rd", "p@ssword", "qwerty", "qwe123", "asdf", "zxcv", "123456",
        "1234", "abcd", "admin", "welcome", "letmein", "iloveyou", "monkey", "dragon", "changeme",
        "!@#$", "@#$%", "#$%^", "inventory", "company", "assets"
    };

    private readonly Microsoft.Extensions.Options.IOptions<SecurityOptions> _options;

    public PasswordPolicy(Microsoft.Extensions.Options.IOptions<SecurityOptions> options) => _options = options;

    public IReadOnlyList<string> Validate(string? password, string? username = null, string? displayName = null)
    {
        var errors = new List<string>();
        var min = _options.Value.PasswordMinimumLength;
        if (string.IsNullOrEmpty(password))
        {
            errors.Add($"Password must be at least {min} characters.");
            return errors;
        }

        if (password.Length < min) errors.Add($"Password must be at least {min} characters.");
        if (password.Length > 128) errors.Add("Password must not exceed 128 characters.");
        if (!password.Any(char.IsUpper)) errors.Add("Password must contain an upper-case letter.");
        if (!password.Any(char.IsLower)) errors.Add("Password must contain a lower-case letter.");
        if (!password.Any(char.IsDigit)) errors.Add("Password must contain a digit.");
        if (password.All(char.IsLetterOrDigit)) errors.Add("Password must contain a symbol.");
        if (password.Distinct().Count() < 6) errors.Add("Password must contain at least 6 different characters.");

        var lower = password.ToLowerInvariant();
        if (ForbiddenFragments.Any(f => lower.Contains(f, StringComparison.Ordinal)))
            errors.Add("Password contains a common word or keyboard pattern.");

        foreach (var part in NameParts(username, displayName))
        {
            if (lower.Contains(part, StringComparison.Ordinal))
            {
                errors.Add("Password must not contain your username or name.");
                break;
            }
        }
        return errors;
    }

    private static IEnumerable<string> NameParts(string? username, string? displayName)
    {
        var separators = new[] { ' ', '.', '_', '-', '@' };
        return new[] { username, displayName }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(s => s!.ToLowerInvariant().Split(separators, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 3)
            .Distinct();
    }
}
