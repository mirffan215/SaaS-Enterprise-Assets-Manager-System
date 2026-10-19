using System.Text.RegularExpressions;
using AMGH.ITInventory.Shared.Errors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMGH.ITInventory.Persistence.Errors;

/// <summary>
/// Converts SQL Server errors into application exceptions with messages safe to show users.
/// Unknown errors return null and propagate unchanged (to be logged, not shown).
/// </summary>
internal static partial class DatabaseExceptionTranslator
{
    private const int DuplicateKeyRow = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int ConstraintConflict = 547;

    private static readonly Dictionary<string, string> FriendlyMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UX_tblAsset_AssetNumber"] = "This asset number is already in use.",
        ["UX_tblAsset_ManufacturerId_SerialNumber"] = "Another asset from this manufacturer already has this serial number.",
        ["UX_tblAsset_Hostname"] = "Another asset already uses this hostname.",
        ["UX_tblAssetAssignment_AssetId"] = "This asset already has a current assignment. Return it first.",
        ["UX_tblAssetAudit_AssetId_AuditCycle"] = "This asset has already been audited in this audit cycle.",
        ["UX_tblAssetDisposal_AssetId"] = "This asset already has an open or completed disposal record.",
        ["UX_tblAssetQRCode_AssetId"] = "This asset already has a current QR code.",
        ["UX_tblUser_Username"] = "This username is already taken.",
        ["UX_tblUser_Email"] = "Another user already has this e-mail address.",
        ["UX_tblUser_EmployeeId"] = "This employee is already linked to another user account.",
        ["UX_tblEmployee_EmployeeNumber"] = "Another employee already has this employee number.",
        ["UX_tblEmployee_Email"] = "Another employee already has this e-mail address.",
        ["UX_tblCurrency_IsBaseCurrency"] = "Only one currency can be the base currency.",
        ["UX_tblSoftwareLicense_LicenseNumber"] = "This license number is already in use.",
    };

    [GeneratedRegex(@"['""](?<name>(?:UX|UQ|PK|CK|FK)_[A-Za-z0-9_]+)['""]", RegexOptions.CultureInvariant)]
    private static partial Regex ConstraintNamePattern();

    public static Exception? Translate(Exception exception)
    {
        if (exception is BusinessRuleException or ConcurrencyConflictException or DuplicateValueException or DataIntegrityException)
            return null; // already translated

        if (exception is DbUpdateConcurrencyException)
            return new ConcurrencyConflictException(exception);

        var sql = FindSqlException(exception);
        if (sql is null) return null;

        var constraint = ConstraintNamePattern().Match(sql.Message) is { Success: true } m ? m.Groups["name"].Value : null;

        return sql.Number switch
        {
            DuplicateKeyRow or UniqueConstraintViolation => new DuplicateValueException(
                constraint is not null && FriendlyMessages.TryGetValue(constraint, out var msg)
                    ? msg
                    : "A record with the same unique value (code, number or name) already exists.",
                constraint, exception),

            ConstraintConflict => new DataIntegrityException(
                constraint is not null && constraint.StartsWith("FK_", StringComparison.OrdinalIgnoreCase)
                    ? "The record refers to an item that does not exist, or is still referenced by other records."
                    : "The data does not meet a validation rule of the system.",
                constraint, exception),

            DatabaseErrorNumbers.NotFound => new NotFoundException(sql.Message, sql.Number, exception),

            >= DatabaseErrorNumbers.NotFound and <= 51999 => new BusinessRuleException(sql.Message, sql.Number, exception),

            _ => null
        };
    }

    private static SqlException? FindSqlException(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is SqlException sql) return sql;
            exception = exception.InnerException;
        }
        return null;
    }
}
