using System.Collections.Immutable;
using System.Security.Claims;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.Services.Oidc;

public interface IOidcConsentLifecycle
{
    Task<OidcConsentResult> EvaluateAsync(OidcConsentRequest request, CancellationToken cancellationToken = default);
    Task<OidcConsentResult> CreatePermanentAuthorizationAsync(
        OidcConsentAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OidcConsentRequest(
    string? ClientId,
    string Subject,
    IReadOnlyList<string> Scopes,
    bool PromptConsent,
    bool PromptNone);

public sealed record OidcConsentAuthorizationRequest(
    string? ClientId,
    string Subject,
    IReadOnlyList<string> Scopes,
    ClaimsPrincipal Principal);

public sealed record OidcConsentResult(OidcConsentOutcome Outcome, string? AuthorizationId = null);

public enum OidcConsentOutcome
{
    Authorized,
    ConsentRequired,
    ConsentPageRequired,
}

internal sealed class OidcConsentLifecycle(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager) : IOidcConsentLifecycle
{
    public async Task<OidcConsentResult> EvaluateAsync(OidcConsentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return new OidcConsentResult(OidcConsentOutcome.Authorized);
        }

        var application = await applicationManager.FindByClientIdAsync(request.ClientId, cancellationToken);
        if (application is null)
        {
            return new OidcConsentResult(OidcConsentOutcome.Authorized);
        }

        if (request.PromptConsent)
        {
            return new OidcConsentResult(OidcConsentOutcome.ConsentPageRequired);
        }

        var consentType = await applicationManager.GetConsentTypeAsync(application, cancellationToken);
        if (!string.Equals(consentType, OpenIddictConstants.ConsentTypes.Explicit, StringComparison.OrdinalIgnoreCase))
        {
            return new OidcConsentResult(OidcConsentOutcome.Authorized);
        }

        var applicationId = await applicationManager.GetIdAsync(application, cancellationToken);
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return new OidcConsentResult(OidcConsentOutcome.Authorized);
        }

        // Consent is intentionally scope-specific: expanding requested scopes must prompt again.
        var authorizations = authorizationManager.FindAsync(
            request.Subject,
            applicationId,
            OpenIddictConstants.Statuses.Valid,
            OpenIddictConstants.AuthorizationTypes.Permanent,
            request.Scopes.ToImmutableArray(),
            cancellationToken);

        await foreach (var authorization in authorizations)
        {
            var authorizationId = await authorizationManager.GetIdAsync(authorization, cancellationToken);
            if (!string.IsNullOrWhiteSpace(authorizationId))
            {
                return new OidcConsentResult(OidcConsentOutcome.Authorized, authorizationId);
            }
        }

        return request.PromptNone
            ? new OidcConsentResult(OidcConsentOutcome.ConsentRequired)
            : new OidcConsentResult(OidcConsentOutcome.ConsentPageRequired);
    }

    public async Task<OidcConsentResult> CreatePermanentAuthorizationAsync(
        OidcConsentAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = string.IsNullOrWhiteSpace(request.ClientId)
            ? null
            : await applicationManager.FindByClientIdAsync(request.ClientId, cancellationToken);

        var applicationId = application is null
            ? null
            : await applicationManager.GetIdAsync(application, cancellationToken);

        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new InvalidOperationException("An explicit consent authorization requires a valid OIDC application.");
        }

        var descriptor = new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = applicationId,
            Principal = request.Principal,
            Status = OpenIddictConstants.Statuses.Valid,
            Subject = request.Subject,
            Type = OpenIddictConstants.AuthorizationTypes.Permanent,
        };

        descriptor.Scopes.UnionWith(request.Scopes);

        var authorization = await authorizationManager.CreateAsync(descriptor, cancellationToken);
        var authorizationId = await authorizationManager.GetIdAsync(authorization, cancellationToken);

        return new OidcConsentResult(
            OidcConsentOutcome.Authorized,
            string.IsNullOrWhiteSpace(authorizationId) ? null : authorizationId);
    }
}
