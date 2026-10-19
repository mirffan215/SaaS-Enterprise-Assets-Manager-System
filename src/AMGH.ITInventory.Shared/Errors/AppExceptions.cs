namespace AMGH.ITInventory.Shared.Errors;

/// <summary>A business rule rejected the operation. The message is safe to show to users.</summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message, int errorNumber = 0, Exception? inner = null)
        : base(message, inner) => ErrorNumber = errorNumber;

    /// <summary>Database error number (see <see cref="DatabaseErrorNumbers"/>) or 0 for application rules.</summary>
    public int ErrorNumber { get; }
}

/// <summary>The requested record does not exist (or is soft-deleted).</summary>
public sealed class NotFoundException : BusinessRuleException
{
    public NotFoundException(string message, int errorNumber = DatabaseErrorNumbers.NotFound, Exception? inner = null)
        : base(message, errorNumber, inner) { }
}

/// <summary>Another user changed the record after it was loaded (ROWVERSION mismatch).</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception? inner = null)
        : base("This record was changed by another user after you opened it. Reload and try again.", inner) { }
}

/// <summary>A unique constraint was violated (duplicate serial number, hostname, code, etc.).</summary>
public sealed class DuplicateValueException : Exception
{
    public DuplicateValueException(string message, string? constraintName = null, Exception? inner = null)
        : base(message, inner) => ConstraintName = constraintName;

    public string? ConstraintName { get; }
}

/// <summary>A database constraint (check / foreign key) rejected the data.</summary>
public sealed class DataIntegrityException : Exception
{
    public DataIntegrityException(string message, string? constraintName = null, Exception? inner = null)
        : base(message, inner) => ConstraintName = constraintName;

    public string? ConstraintName { get; }
}
