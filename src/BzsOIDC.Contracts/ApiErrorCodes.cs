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
