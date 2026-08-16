using System.Security.Cryptography;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.Services.Oidc;

internal static class OidcClientDescriptorAdapter
{
    internal static OpenIddictApplicationDescriptor CreateDescriptor(
        OidcClientProfileEvaluation evaluation,
        string clientId)
    {
        var request = evaluation.Request;
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = request.DisplayName,
            ClientType = request.PublicClient
                ? OpenIddictConstants.ClientTypes.Public
                : OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = ToOpenIddictConsentType(evaluation.ConsentType),
        };

        if (!request.PublicClient)
        {
            descriptor.ClientSecret = request.ClientSecret ?? GenerateClientSecret();
        }

        descriptor.Permissions.UnionWith(evaluation.Permissions);

        if (request.RequireProofKeyForCodeExchange)
        {
            descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
        }

        foreach (var uri in request.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        foreach (var uri in request.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        return descriptor;
    }

    private static string GenerateClientSecret()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static string ToOpenIddictConsentType(OidcClientConsentType consentType)
    {
        return consentType switch
        {
            OidcClientConsentType.Implicit => OpenIddictConstants.ConsentTypes.Implicit,
            OidcClientConsentType.Explicit => OpenIddictConstants.ConsentTypes.Explicit,
            OidcClientConsentType.External => OpenIddictConstants.ConsentTypes.External,
            _ => throw new ArgumentOutOfRangeException(nameof(consentType), consentType, null),
        };
    }
}
