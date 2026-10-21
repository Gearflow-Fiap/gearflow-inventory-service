namespace GearFlow.Inventory.Domain.Core;

/// <summary>
/// Resultado de uma operação de negócio. Falha de regra vira <see cref="Error"/>, nunca exceção
/// (convenção herdada do monólito).
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None) throw new InvalidOperationException();
        if (!isSuccess && error == Error.None) throw new InvalidOperationException();

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

public class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error) : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Não é possível acessar Value de um resultado de falha.");
}

/// <summary>
/// Intenção semântica da falha; a camada HTTP escolhe o status a partir dela (ver ResultExtensions na Api).
/// </summary>
public enum ErrorType
{
    Failure = 0,
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
}

public record Error(
    string Code,
    string Description,
    ErrorType Type = ErrorType.Failure,
    string? Field = null)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Validation(string code, string description, string? field = null) =>
        new(code, description, ErrorType.Validation, field);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
}
