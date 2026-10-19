namespace AMGH.ITInventory.Shared.Results;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    BusinessRule,
    Unexpected
}

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string message, string code = "validation") => new(code, message, ErrorType.Validation);
    public static Error NotFound(string message, string code = "not_found") => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string message, string code = "conflict") => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string message, string code = "forbidden") => new(code, message, ErrorType.Forbidden);
    public static Error BusinessRule(string message, string code = "business_rule") => new(code, message, ErrorType.BusinessRule);
}

/// <summary>Outcome of an application operation without exceptions for expected failures.</summary>
public class Result
{
    protected Result(bool isSuccess, IReadOnlyList<Error> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public IReadOnlyList<Error> Errors { get; }
    public Error? FirstError => Errors.Count > 0 ? Errors[0] : null;

    public static Result Success() => new(true, Array.Empty<Error>());
    public static Result Failure(params Error[] errors)
    {
        if (errors is null || errors.Length == 0) throw new ArgumentException("At least one error is required.", nameof(errors));
        return new Result(false, errors);
    }
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, bool isSuccess, IReadOnlyList<Error> errors) : base(isSuccess, errors) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot read the value of a failed result.");

    public static Result<T> Success(T value) => new(value, true, Array.Empty<Error>());
    public static new Result<T> Failure(params Error[] errors)
    {
        if (errors is null || errors.Length == 0) throw new ArgumentException("At least one error is required.", nameof(errors));
        return new Result<T>(default, false, errors);
    }

    public static implicit operator Result<T>(T value) => Success(value);
}
