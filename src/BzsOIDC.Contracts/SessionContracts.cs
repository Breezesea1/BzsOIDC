namespace BzsOIDC.Contracts;

/// <summary>Display-only summary of the current cookie session.</summary>
public sealed class SessionSummary
{
    public bool IsAuthenticated { get; init; }
    public string? UserId { get; init; }
    public string? UserName { get; init; }
    public string? DisplayName { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
}

/// <summary>Metadata returned when bootstrapping an antiforgery request token.</summary>
public sealed class AntiforgeryTokenResponse
{
    public string Token { get; init; } = string.Empty;
    public string HeaderName { get; init; } = "RequestVerificationToken";
    public string FormFieldName { get; init; } = "__RequestVerificationToken";
}
