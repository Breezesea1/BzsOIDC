using BzsOIDC.Shared.Infrastructure.Authorization;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.Services.Oidc;

public interface IOidcClientProfile
{
    OidcClientUpsertRequest CreatePreset(OidcClientPresetRequest preset);
    OidcClientUpsertRequest CreateRequest(OidcClientProfileDraft draft);
    OidcClientConsentType GetDefaultConsentType(OidcClientAuthFlow authFlow);
    OidcClientProfileEvaluation Evaluate(OidcClientUpsertRequest request);
    OidcClientPersistedProfileInterpretation InterpretPersistedProfile(OidcClientPersistedProfile profile);
}

public sealed class OidcClientProfileDraft
{
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public OidcClientAuthFlow AuthFlow { get; init; }
    public OidcClientConsentType ConsentType { get; init; }
    public string[] Scopes { get; init; } = [];
    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
}

public sealed class OidcClientProfileEvaluation
{
    public OidcClientUpsertRequest Request { get; init; } = new();
    public OidcClientAuthFlow? AuthFlow { get; init; }
    public OidcClientConsentType ConsentType { get; init; }
    public string[] Permissions { get; init; } = [];
    public string[] Errors { get; init; } = [];
}

public sealed class OidcClientPersistedProfile
{
    public string? ClientType { get; init; }
    public string? ConsentType { get; init; }
    public string[] Permissions { get; init; } = [];
    public string[] RedirectUris { get; init; } = [];
}

public sealed class OidcClientPersistedProfileInterpretation
{
    public OidcClientAuthFlow AuthFlow { get; init; }
    public bool PublicClient { get; init; }
    public OidcClientConsentType ConsentType { get; init; }
    public string[] GrantTypes { get; init; } = [];
    public string[] Scopes { get; init; } = [];
}

internal sealed class OidcClientProfile : IOidcClientProfile
{
    public OidcClientUpsertRequest CreatePreset(OidcClientPresetRequest preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return preset.Kind switch
        {
            OidcClientPresetKind.Spa => CreateInteractivePreset(preset, "SPA client"),
            OidcClientPresetKind.ServerWeb => CreateInteractivePreset(preset, "Server web client"),
            OidcClientPresetKind.MachineToMachine => new OidcClientUpsertRequest
            {
                ClientId = preset.ClientId,
                DisplayName = preset.DisplayName ?? "Machine client",
                AuthFlow = OidcClientAuthFlow.ClientCredentials,
                PublicClient = false,
                RequireProofKeyForCodeExchange = false,
                ConsentType = OidcClientConsentType.External,
                GrantTypes = [OpenIddictConstants.GrantTypes.ClientCredentials],
                Scopes = NormalizeScopes(preset.Scopes, [PermissionConstants.ScopeApi]),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(preset.Kind), preset.Kind, null),
        };
    }

    private static OidcClientUpsertRequest CreateInteractivePreset(
        OidcClientPresetRequest preset,
        string fallbackDisplayName)
    {
        return new OidcClientUpsertRequest
        {
            ClientId = preset.ClientId,
            DisplayName = preset.DisplayName ?? fallbackDisplayName,
            AuthFlow = OidcClientAuthFlow.AuthorizationCode,
            PublicClient = true,
            RequireProofKeyForCodeExchange = true,
            ConsentType = OidcClientConsentType.Implicit,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.RefreshToken],
            Scopes = NormalizeScopes(preset.Scopes, [PermissionConstants.ScopeApi]),
            RedirectUris = ["https://localhost:5001/signin-oidc"],
            PostLogoutRedirectUris = ["https://localhost:5001/signout-callback-oidc"],
        };
    }

    public OidcClientUpsertRequest CreateRequest(OidcClientProfileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return draft.AuthFlow switch
        {
            OidcClientAuthFlow.AuthorizationCode => new OidcClientUpsertRequest
            {
                ClientId = draft.ClientId,
                DisplayName = draft.DisplayName,
                AuthFlow = OidcClientAuthFlow.AuthorizationCode,
                PublicClient = true,
                RequireProofKeyForCodeExchange = true,
                ConsentType = draft.ConsentType,
                GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.RefreshToken],
                Scopes = draft.Scopes,
                RedirectUris = draft.RedirectUris,
                PostLogoutRedirectUris = draft.PostLogoutRedirectUris,
            },
            OidcClientAuthFlow.ClientCredentials => new OidcClientUpsertRequest
            {
                ClientId = draft.ClientId,
                ClientSecret = draft.ClientSecret,
                DisplayName = draft.DisplayName,
                AuthFlow = OidcClientAuthFlow.ClientCredentials,
                PublicClient = false,
                RequireProofKeyForCodeExchange = false,
                ConsentType = OidcClientConsentType.External,
                GrantTypes = [OpenIddictConstants.GrantTypes.ClientCredentials],
                Scopes = draft.Scopes,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(draft.AuthFlow), draft.AuthFlow, null),
        };
    }

    public OidcClientConsentType GetDefaultConsentType(OidcClientAuthFlow authFlow)
    {
        return ResolveDefaultConsentType(authFlow);
    }

    public OidcClientProfileEvaluation Evaluate(OidcClientUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = new OidcClientUpsertRequest
        {
            ClientId = NormalizeOptional(request.ClientId),
            ClientSecret = NormalizeOptional(request.ClientSecret),
            DisplayName = request.DisplayName?.Trim() ?? string.Empty,
            AuthFlow = request.AuthFlow,
            PublicClient = request.PublicClient,
            RequireProofKeyForCodeExchange = request.RequireProofKeyForCodeExchange,
            ConsentType = request.ConsentType,
            GrantTypes = NormalizeValues(request.GrantTypes),
            Scopes = NormalizeValues(request.Scopes),
            RedirectUris = NormalizeUris(request.RedirectUris),
            PostLogoutRedirectUris = NormalizeUris(request.PostLogoutRedirectUris),
        };
        var errors = new List<string>();
        var authFlow = ResolveAuthFlow(normalizedRequest, out var authFlowError);

        if (normalizedRequest.AuthFlow is not null && !Enum.IsDefined(normalizedRequest.AuthFlow.Value))
        {
            errors.Add("AuthFlow is invalid.");
        }

        if (normalizedRequest.ConsentType is not null && !Enum.IsDefined(normalizedRequest.ConsentType.Value))
        {
            errors.Add("ConsentType is invalid.");
        }
        else if (normalizedRequest.ConsentType is OidcClientConsentType.Unknown)
        {
            errors.Add("ConsentType is invalid.");
        }

        if (authFlowError is not null)
        {
            errors.Add(authFlowError);
        }

        if (string.IsNullOrWhiteSpace(normalizedRequest.DisplayName))
        {
            errors.Add("DisplayName is required.");
        }

        if (normalizedRequest.PublicClient && !string.IsNullOrWhiteSpace(normalizedRequest.ClientSecret))
        {
            errors.Add("Public clients must not specify ClientSecret.");
        }

        if (normalizedRequest.GrantTypes.Length == 0)
        {
            errors.Add("At least one grant type must be provided.");
        }

        var hasAuthorizationCode = normalizedRequest.GrantTypes.Contains(OpenIddictConstants.GrantTypes.AuthorizationCode, StringComparer.OrdinalIgnoreCase);
        if (hasAuthorizationCode && normalizedRequest.RedirectUris.Length == 0)
        {
            errors.Add("Authorization code clients must provide at least one redirect URI.");
        }

        if (authFlow is OidcClientAuthFlow.AuthorizationCode)
        {
            if (normalizedRequest.ConsentType is OidcClientConsentType.External)
            {
                errors.Add("Authorization Code Flow clients only support implicit or explicit consent.");
            }

            if (!normalizedRequest.PublicClient)
            {
                errors.Add("Authorization Code Flow clients must be public clients in the current onboarding.");
            }

            if (!normalizedRequest.GrantTypes.All(static grantType =>
                    string.Equals(grantType, OpenIddictConstants.GrantTypes.AuthorizationCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(grantType, OpenIddictConstants.GrantTypes.RefreshToken, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("Authorization Code Flow clients only support authorization_code and refresh_token grants.");
            }

            if (!normalizedRequest.RequireProofKeyForCodeExchange)
            {
                errors.Add("Authorization Code Flow clients must require PKCE.");
            }
        }

        if (authFlow is OidcClientAuthFlow.ClientCredentials)
        {
            if (normalizedRequest.ConsentType is not null && normalizedRequest.ConsentType != OidcClientConsentType.External)
            {
                errors.Add("Client Credentials Flow clients must use external consent.");
            }

            if (normalizedRequest.PublicClient)
            {
                errors.Add("Client Credentials Flow clients must be confidential clients.");
            }

            if (normalizedRequest.GrantTypes.Length != 1 ||
                !string.Equals(normalizedRequest.GrantTypes[0], OpenIddictConstants.GrantTypes.ClientCredentials, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Client Credentials Flow clients only support the client_credentials grant.");
            }

            if (normalizedRequest.RedirectUris.Length > 0)
            {
                errors.Add("Client Credentials Flow clients must not configure redirect URIs.");
            }

            if (normalizedRequest.PostLogoutRedirectUris.Length > 0)
            {
                errors.Add("Client Credentials Flow clients must not configure post logout redirect URIs.");
            }
        }

        if (!TryValidateAbsoluteUris(normalizedRequest.RedirectUris, out var redirectError))
        {
            errors.Add(redirectError!);
        }

        if (!TryValidateAbsoluteUris(normalizedRequest.PostLogoutRedirectUris, out var postLogoutRedirectError))
        {
            errors.Add(postLogoutRedirectError!);
        }

        return new OidcClientProfileEvaluation
        {
            Request = normalizedRequest,
            AuthFlow = authFlow,
            ConsentType = normalizedRequest.ConsentType ?? ResolveDefaultConsentType(authFlow),
            Permissions = BuildPermissions(normalizedRequest),
            Errors = errors.ToArray(),
        };
    }

    public OidcClientPersistedProfileInterpretation InterpretPersistedProfile(OidcClientPersistedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var grantTypes = profile.Permissions
            .Where(static permission => permission.StartsWith(OpenIddictConstants.Permissions.Prefixes.GrantType, StringComparison.OrdinalIgnoreCase))
            .Select(static permission => permission[OpenIddictConstants.Permissions.Prefixes.GrantType.Length..])
            .ToArray();
        var scopes = profile.Permissions
            .Where(static permission => permission.StartsWith(OpenIddictConstants.Permissions.Prefixes.Scope, StringComparison.OrdinalIgnoreCase))
            .Select(static permission => permission[OpenIddictConstants.Permissions.Prefixes.Scope.Length..])
            .ToArray();
        var publicClient = string.Equals(profile.ClientType, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase);
        var interactive = publicClient &&
            profile.RedirectUris.Length > 0 &&
            grantTypes.All(static grantType =>
                string.Equals(grantType, OpenIddictConstants.GrantTypes.AuthorizationCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(grantType, OpenIddictConstants.GrantTypes.RefreshToken, StringComparison.OrdinalIgnoreCase));

        return new OidcClientPersistedProfileInterpretation
        {
            AuthFlow = interactive ? OidcClientAuthFlow.AuthorizationCode : OidcClientAuthFlow.ClientCredentials,
            PublicClient = publicClient,
            ConsentType = FromPersistedConsentType(profile.ConsentType),
            GrantTypes = grantTypes,
            Scopes = scopes,
        };
    }

    private static string[] BuildPermissions(OidcClientUpsertRequest request)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasAuthorizationCode = request.GrantTypes.Contains(OpenIddictConstants.GrantTypes.AuthorizationCode, StringComparer.OrdinalIgnoreCase);
        var hasRefreshToken = request.GrantTypes.Contains(OpenIddictConstants.GrantTypes.RefreshToken, StringComparer.OrdinalIgnoreCase);
        var hasClientCredentials = request.GrantTypes.Contains(OpenIddictConstants.GrantTypes.ClientCredentials, StringComparer.OrdinalIgnoreCase);

        if (hasAuthorizationCode)
        {
            permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
        }

        if (hasAuthorizationCode || hasRefreshToken || hasClientCredentials)
        {
            permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (hasRefreshToken)
        {
            permissions.Add(OpenIddictConstants.Permissions.Endpoints.Revocation);
        }

        if (hasClientCredentials && !request.PublicClient)
        {
            permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
        }

        if (request.PostLogoutRedirectUris.Length > 0)
        {
            permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);
        }

        foreach (var grantType in request.GrantTypes)
        {
            permissions.Add(OpenIddictConstants.Permissions.Prefixes.GrantType + grantType);
        }

        foreach (var scope in request.Scopes)
        {
            permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
        }

        return permissions.ToArray();
    }

    private static OidcClientAuthFlow? ResolveAuthFlow(OidcClientUpsertRequest request, out string? error)
    {
        if (request.AuthFlow is not null)
        {
            error = null;
            return request.AuthFlow.Value;
        }

        var isInteractive = request.PublicClient &&
            request.GrantTypes.Length > 0 &&
            request.GrantTypes.All(static grantType =>
                string.Equals(grantType, OpenIddictConstants.GrantTypes.AuthorizationCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(grantType, OpenIddictConstants.GrantTypes.RefreshToken, StringComparison.OrdinalIgnoreCase));

        if (isInteractive)
        {
            error = null;
            return OidcClientAuthFlow.AuthorizationCode;
        }

        var isMachine = !request.PublicClient &&
            request.GrantTypes.Length == 1 &&
            string.Equals(request.GrantTypes[0], OpenIddictConstants.GrantTypes.ClientCredentials, StringComparison.OrdinalIgnoreCase);

        if (isMachine)
        {
            error = null;
            return OidcClientAuthFlow.ClientCredentials;
        }

        error = "Current onboarding only supports Authorization Code Flow (public + auth code/refresh token + PKCE) and Client Credentials Flow (confidential + client_credentials) clients.";
        return null;
    }

    private static OidcClientConsentType ResolveDefaultConsentType(OidcClientAuthFlow? authFlow)
    {
        return authFlow switch
        {
            OidcClientAuthFlow.AuthorizationCode => OidcClientConsentType.Implicit,
            OidcClientAuthFlow.ClientCredentials => OidcClientConsentType.External,
            _ => OidcClientConsentType.Explicit,
        };
    }

    private static OidcClientConsentType FromPersistedConsentType(string? consentType)
    {
        return consentType switch
        {
            OpenIddictConstants.ConsentTypes.Implicit => OidcClientConsentType.Implicit,
            OpenIddictConstants.ConsentTypes.External => OidcClientConsentType.External,
            OpenIddictConstants.ConsentTypes.Explicit => OidcClientConsentType.Explicit,
            null or "" => OidcClientConsentType.Explicit,
            _ => OidcClientConsentType.Unknown,
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string[] NormalizeValues(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeUris(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryValidateAbsoluteUris(IEnumerable<string> uris, out string? error)
    {
        foreach (var uri in uris)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
            {
                error = $"Invalid URI: '{uri}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static string[] NormalizeScopes(IEnumerable<string>? scopes, IReadOnlyList<string> fallbackScopes)
    {
        var normalized = (scopes ?? [])
            .Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Select(static scope => scope.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length > 0 ? normalized : fallbackScopes.ToArray();
    }
}
