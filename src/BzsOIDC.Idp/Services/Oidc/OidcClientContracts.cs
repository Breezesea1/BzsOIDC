using BzsOIDC.Shared.Infrastructure.Authorization;

namespace BzsOIDC.Idp.Services.Oidc;

public static class OidcGrantTypes
{
    public const string AuthorizationCode = "authorization_code";
    public const string RefreshToken = "refresh_token";
    public const string ClientCredentials = "client_credentials";
}

public sealed class OidcClientUpsertRequest
{
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public OidcClientAuthFlow? AuthFlow { get; init; }
    public bool PublicClient { get; init; }
    public bool RequireProofKeyForCodeExchange { get; init; } = true;
    public OidcClientConsentType? ConsentType { get; init; }
    public string[] GrantTypes { get; init; } = [OidcGrantTypes.AuthorizationCode];
    public string[] Scopes { get; init; } = [PermissionConstants.ScopeApi];
    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
}

public sealed class OidcClientPresetRequest
{
    public OidcClientPresetKind Kind { get; init; }
    public string? ClientId { get; init; }
    public string? DisplayName { get; init; }
    public string[] Scopes { get; init; } = [];
}

public enum OidcClientPresetKind
{
    Spa,
    ServerWeb,
    MachineToMachine,
}

public sealed class OidcClientResponse
{
    public string ClientId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public OidcClientAuthFlow AuthFlow { get; init; }
    public bool PublicClient { get; init; }
    public OidcClientConsentType ConsentType { get; init; }
    public string[] GrantTypes { get; init; } = [];
    public string[] Scopes { get; init; } = [];
    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
    public string[] Permissions { get; init; } = [];
    public string[] Requirements { get; init; } = [];
}

public sealed class OidcClientRegistrationResponse
{
    public string ClientId { get; init; } = string.Empty;
    public string? ClientSecret { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public OidcClientAuthFlow AuthFlow { get; init; }
}

public enum OidcClientAuthFlow
{
    AuthorizationCode,
    ClientCredentials,
}

public enum OidcClientConsentType
{
    Unknown = -1,
    Implicit,
    Explicit,
    External,
}

public enum OidcClientCommandStatus
{
    Success,
    ValidationFailed,
    Conflict,
    NotFound,
}

public sealed class OidcClientCommandResult<T>
{
    public OidcClientCommandStatus Status { get; init; }
    public T? Value { get; init; }
    public string[] Errors { get; init; } = [];
}
