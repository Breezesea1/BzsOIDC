using Microsoft.AspNetCore.DataProtection;

namespace BzsOIDC.Idp.Services.Oidc;

public interface IOidcConsentRequestProtector
{
    string Protect(string queryString);
    bool TryUnprotect(string token, out string queryString);
}

internal sealed class OidcConsentRequestProtector(IDataProtectionProvider dataProtectionProvider)
    : IOidcConsentRequestProtector
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("BzsOIDC.OidcConsent.v1");

    public string Protect(string queryString)
    {
        ArgumentNullException.ThrowIfNull(queryString);
        return _protector.Protect(queryString);
    }

    public bool TryUnprotect(string token, out string queryString)
    {
        queryString = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            queryString = _protector.Unprotect(token);
            return !string.IsNullOrWhiteSpace(queryString) && queryString.StartsWith("?", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
