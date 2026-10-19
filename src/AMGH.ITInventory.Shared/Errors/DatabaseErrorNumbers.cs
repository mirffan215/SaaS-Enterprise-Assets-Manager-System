namespace AMGH.ITInventory.Shared.Errors;

/// <summary>Custom error numbers raised by stored procedures (5000x) and triggers (510xx).</summary>
public static class DatabaseErrorNumbers
{
    public const int NotFound = 50001;
    public const int InvalidState = 50002;
    public const int ValidationFailed = 50003;
    public const int SegregationOfDuties = 50004;

    public const int SequenceDecrease = 51001;
    public const int SequenceImmutable = 51002;
    public const int SequenceDelete = 51003;
    public const int AssetHardDelete = 51004;
    public const int LicenseHardDelete = 51005;
    public const int LicenseNumberChange = 51006;
    public const int AssetNumberChange = 51007;
    public const int AppendOnlyLog = 51010;
    public const int SeatLimitExceeded = 51020;
    public const int InactiveLicense = 51021;
    public const int DisposalEvidenceMissing = 51030;
    public const int DisposalCertificateMissing = 51031;
    public const int DisposalReopen = 51032;
    public const int SystemRoleProtected = 51040;
    public const int LastAdministrator = 51042;

    /// <summary>True for errors that represent a business-rule rejection safe to show to users.</summary>
    public static bool IsBusinessRule(int number) => number is >= 50001 and <= 51999;
}
