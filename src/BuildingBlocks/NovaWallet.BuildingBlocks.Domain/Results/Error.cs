namespace NovaWallet.BuildingBlocks.Domain.Results;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
}

public sealed record Error(string Code, string Description, ErrorType Type = ErrorType.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
}
