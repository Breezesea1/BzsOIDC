namespace BzsOIDC.Contracts;

/// <summary>Stable, non-localized codes returned by HTTP APIs.</summary>
public static class ApiErrorCodes
{
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string PreconditionRequired = "precondition_required";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string AntiforgeryFailed = "antiforgery_failed";
    public const string InvalidCredentials = "invalid_credentials";
    public const string Unexpected = "unexpected_error";
}

/// <summary>Compatibility name for consumers that prefer the shorter error-code type.</summary>
public static class ErrorCodes
{
    public const string Unauthorized = ApiErrorCodes.Unauthorized;
    public const string Forbidden = ApiErrorCodes.Forbidden;
    public const string ValidationFailed = ApiErrorCodes.ValidationFailed;
    public const string NotFound = ApiErrorCodes.NotFound;
    public const string Conflict = ApiErrorCodes.Conflict;
    public const string PreconditionRequired = ApiErrorCodes.PreconditionRequired;
    public const string ConcurrencyConflict = ApiErrorCodes.ConcurrencyConflict;
    public const string AntiforgeryFailed = ApiErrorCodes.AntiforgeryFailed;
    public const string InvalidCredentials = ApiErrorCodes.InvalidCredentials;
    public const string Unexpected = ApiErrorCodes.Unexpected;
}
