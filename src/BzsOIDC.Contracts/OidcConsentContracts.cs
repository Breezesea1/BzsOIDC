namespace BzsOIDC.Contracts;

public sealed record OidcConsentScopePreview(
    string Name,
    string? DisplayName,
    string? Description);

public sealed record OidcConsentPreviewResponse(
    string ClientId,
    string DisplayName,
    IReadOnlyList<OidcConsentScopePreview> Scopes,
    string AuthorizeUrl,
    string RequestToken);
