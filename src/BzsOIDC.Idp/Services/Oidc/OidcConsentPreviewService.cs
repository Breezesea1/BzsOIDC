using BzsOIDC.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.Services.Oidc;

public interface IOidcConsentPreviewService
{
    Task<OidcConsentPreviewResponse?> GetPreviewAsync(
        string requestToken,
        CancellationToken cancellationToken = default);
}

internal sealed class OidcConsentPreviewService(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IOidcConsentRequestProtector requestProtector) : IOidcConsentPreviewService
{
    public async Task<OidcConsentPreviewResponse?> GetPreviewAsync(
        string requestToken,
        CancellationToken cancellationToken = default)
    {
        if (!requestProtector.TryUnprotect(requestToken, out var originalQuery))
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(originalQuery);
        if (!query.TryGetValue("client_id", out var clientIdValues) ||
            string.IsNullOrWhiteSpace(clientIdValues.ToString()))
        {
            return null;
        }

        var clientId = clientIdValues.ToString();
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null || !await IsValidRequestAsync(application, query, cancellationToken))
        {
            return null;
        }

        var requestedScopes = query.TryGetValue("scope", out var scopeValues)
            ? scopeValues.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        var scopes = new List<OidcConsentScopePreview>();
        foreach (var scopeName in requestedScopes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var scope = await scopeManager.FindByNameAsync(scopeName, cancellationToken);
            scopes.Add(new OidcConsentScopePreview(
                scopeName,
                scope is null ? scopeName : await scopeManager.GetDisplayNameAsync(scope, cancellationToken),
                scope is null ? null : await scopeManager.GetDescriptionAsync(scope, cancellationToken)));
        }

        var authorizeQuery = originalQuery.TrimStart('?');
        return new OidcConsentPreviewResponse(
            clientId,
            await applicationManager.GetDisplayNameAsync(application, cancellationToken) ?? clientId,
            scopes,
            $"/connect/authorize?{authorizeQuery}&consent_request={Uri.EscapeDataString(requestToken)}",
            requestToken);
    }

    private async Task<bool> IsValidRequestAsync(
        object application,
        IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query,
        CancellationToken cancellationToken)
    {
        if (!query.TryGetValue("response_type", out var responseType) ||
            !string.Equals(responseType.ToString(), OpenIddictConstants.ResponseTypes.Code, StringComparison.Ordinal))
        {
            return false;
        }

        if (!query.TryGetValue("redirect_uri", out var redirectUriValues) ||
            !Uri.TryCreate(redirectUriValues.ToString(), UriKind.Absolute, out var redirectUri))
        {
            return false;
        }

        var redirects = await applicationManager.GetRedirectUrisAsync(application, cancellationToken);
        if (!redirects.Any(uri => Uri.TryCreate(uri, UriKind.Absolute, out var candidate) && candidate == redirectUri))
        {
            return false;
        }

        var permissions = await applicationManager.GetPermissionsAsync(application, cancellationToken);
        if (!permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Authorization, StringComparer.Ordinal))
        {
            return false;
        }

        var scopes = query.TryGetValue("scope", out var scopeValues)
            ? scopeValues.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        return scopes.All(scope => permissions.Contains(
            OpenIddictConstants.Permissions.Prefixes.Scope + scope,
            StringComparer.Ordinal));
    }
}
