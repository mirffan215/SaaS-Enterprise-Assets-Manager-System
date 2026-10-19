namespace AMGH.ITInventory.Domain.Enums;

/// <summary>Stored as NVARCHAR(30) text; values are enforced by a CHECK constraint.</summary>
public enum LoginEventType
{
    LoginSuccess,
    LoginFailed,
    Logout,
    Lockout,
    PasswordChanged,
    PasswordReset,
    SessionExpired,
    AccountDisabled
}
