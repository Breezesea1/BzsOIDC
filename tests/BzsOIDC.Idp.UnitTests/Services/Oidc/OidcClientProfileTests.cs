using BzsOIDC.Idp.Services.Oidc;

namespace BzsOIDC.Idp.UnitTests.Services.Oidc;

public sealed class OidcClientProfileTests
{
    [Fact]
    public void CreatePreset_WhenSpaScopesMissing_UsesInteractiveDefaultsAndApiScope()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var preset = profile.CreatePreset(new OidcClientPresetRequest
        {
            Kind = OidcClientPresetKind.Spa,
        });

        Assert.Equal(OidcClientAuthFlow.AuthorizationCode, preset.AuthFlow);
        Assert.True(preset.PublicClient);
        Assert.True(preset.RequireProofKeyForCodeExchange);
        Assert.Equal(OidcClientConsentType.Implicit, preset.ConsentType);
        Assert.Equal(["authorization_code", "refresh_token"], preset.GrantTypes);
        Assert.Equal(["api"], preset.Scopes);
    }

    [Fact]
    public void CreatePreset_WhenServerWebSelected_UsesServerDisplayNameAndInteractiveDefaults()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var preset = profile.CreatePreset(new OidcClientPresetRequest
        {
            Kind = OidcClientPresetKind.ServerWeb,
        });

        Assert.Equal("Server web client", preset.DisplayName);
        Assert.Equal(OidcClientAuthFlow.AuthorizationCode, preset.AuthFlow);
        Assert.True(preset.PublicClient);
        Assert.True(preset.RequireProofKeyForCodeExchange);
        Assert.Equal(OidcClientConsentType.Implicit, preset.ConsentType);
        Assert.Equal(["authorization_code", "refresh_token"], preset.GrantTypes);
        Assert.Equal(["api"], preset.Scopes);
    }

    [Fact]
    public void CreatePreset_WhenMachineToMachine_ReturnsConfidentialClientCredentialsProfile()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var preset = profile.CreatePreset(new OidcClientPresetRequest
        {
            Kind = OidcClientPresetKind.MachineToMachine,
            ClientId = "machine-client",
            DisplayName = "Machine client",
            Scopes = ["api"],
        });

        Assert.Equal("machine-client", preset.ClientId);
        Assert.Equal(OidcClientAuthFlow.ClientCredentials, preset.AuthFlow);
        Assert.False(preset.PublicClient);
        Assert.False(preset.RequireProofKeyForCodeExchange);
        Assert.Equal(OidcClientConsentType.External, preset.ConsentType);
        Assert.Equal(["client_credentials"], preset.GrantTypes);
        Assert.Equal(["api"], preset.Scopes);
        Assert.Empty(preset.RedirectUris);
        Assert.Empty(preset.PostLogoutRedirectUris);
    }

    [Fact]
    public void Evaluate_WhenMachineClientIsValid_NormalizesScopesAndDerivesPermissions()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var evaluation = profile.Evaluate(new OidcClientUpsertRequest
        {
            DisplayName = "Machine client",
            AuthFlow = OidcClientAuthFlow.ClientCredentials,
            PublicClient = false,
            RequireProofKeyForCodeExchange = false,
            ConsentType = OidcClientConsentType.External,
            GrantTypes = [" client_credentials "],
            Scopes = [" api ", "API"],
        });

        Assert.Empty(evaluation.Errors);
        Assert.Equal(OidcClientAuthFlow.ClientCredentials, evaluation.AuthFlow);
        Assert.Equal(OidcClientConsentType.External, evaluation.ConsentType);
        Assert.Equal(["api"], evaluation.Request.Scopes);
        Assert.Contains("ept:token", evaluation.Permissions);
        Assert.Contains("ept:introspection", evaluation.Permissions);
        Assert.Contains("gt:client_credentials", evaluation.Permissions);
        Assert.Contains("scp:api", evaluation.Permissions);
        Assert.DoesNotContain("ept:authorization", evaluation.Permissions);
    }

    [Fact]
    public void InterpretPersistedProfile_WhenHistoricalClientIsNotPublicInteractive_FallsBackToClientCredentials()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var interpretation = profile.InterpretPersistedProfile(new OidcClientPersistedProfile
        {
            ClientType = "confidential",
            ConsentType = "custom-consent",
            Permissions = ["gt:authorization_code", "gt:refresh_token", "scp:api"],
            RedirectUris = ["https://client.example.com/callback"],
        });

        Assert.Equal(OidcClientAuthFlow.ClientCredentials, interpretation.AuthFlow);
        Assert.False(interpretation.PublicClient);
        Assert.Equal(OidcClientConsentType.Unknown, interpretation.ConsentType);
        Assert.Equal(["authorization_code", "refresh_token"], interpretation.GrantTypes);
        Assert.Equal(["api"], interpretation.Scopes);
    }

    [Fact]
    public void CreateRequest_WhenInteractiveDraft_ReturnsProfileManagedGrantAndPkceSettings()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var request = profile.CreateRequest(new OidcClientProfileDraft
        {
            ClientId = "interactive-client",
            DisplayName = "Interactive client",
            AuthFlow = OidcClientAuthFlow.AuthorizationCode,
            ConsentType = OidcClientConsentType.Explicit,
            Scopes = ["api"],
            RedirectUris = ["https://client.example.com/callback"],
        });

        Assert.True(request.PublicClient);
        Assert.True(request.RequireProofKeyForCodeExchange);
        Assert.Equal(OidcClientConsentType.Explicit, request.ConsentType);
        Assert.Equal(["authorization_code", "refresh_token"], request.GrantTypes);
    }

    [Theory]
    [InlineData(OidcClientAuthFlow.AuthorizationCode, OidcClientConsentType.Implicit)]
    [InlineData(OidcClientAuthFlow.ClientCredentials, OidcClientConsentType.External)]
    public void GetDefaultConsentType_WhenFlowSelected_ReturnsProfileConsentDefault(
        OidcClientAuthFlow authFlow,
        OidcClientConsentType expectedConsentType)
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var consentType = profile.GetDefaultConsentType(authFlow);

        Assert.Equal(expectedConsentType, consentType);
    }

    [Fact]
    public void Evaluate_WhenAuthorizationCodeProfileViolatesRules_ReturnsStableValidationErrors()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var evaluation = profile.Evaluate(new OidcClientUpsertRequest
        {
            DisplayName = "Interactive client",
            AuthFlow = OidcClientAuthFlow.AuthorizationCode,
            PublicClient = false,
            RequireProofKeyForCodeExchange = false,
            ConsentType = OidcClientConsentType.External,
            GrantTypes = ["authorization_code", "client_credentials"],
        });

        Assert.Equal(
        [
            "Authorization code clients must provide at least one redirect URI.",
            "Authorization Code Flow clients only support implicit or explicit consent.",
            "Authorization Code Flow clients must be public clients in the current onboarding.",
            "Authorization Code Flow clients only support authorization_code and refresh_token grants.",
            "Authorization Code Flow clients must require PKCE.",
        ], evaluation.Errors);
    }

    [Fact]
    public void Evaluate_WhenClientCredentialsProfileViolatesRules_ReturnsStableValidationErrors()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var evaluation = profile.Evaluate(new OidcClientUpsertRequest
        {
            DisplayName = "Machine client",
            AuthFlow = OidcClientAuthFlow.ClientCredentials,
            PublicClient = true,
            ConsentType = OidcClientConsentType.Explicit,
            GrantTypes = ["client_credentials", "refresh_token"],
            RedirectUris = ["https://client.example.com/callback"],
            PostLogoutRedirectUris = ["https://client.example.com/signout"],
        });

        Assert.Equal(
        [
            "Client Credentials Flow clients must use external consent.",
            "Client Credentials Flow clients must be confidential clients.",
            "Client Credentials Flow clients only support the client_credentials grant.",
            "Client Credentials Flow clients must not configure redirect URIs.",
            "Client Credentials Flow clients must not configure post logout redirect URIs.",
        ], evaluation.Errors);
    }

    [Fact]
    public void Evaluate_WhenProfileAndConsentEnumsAreInvalid_ReturnsStableValidationErrors()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var evaluation = profile.Evaluate(new OidcClientUpsertRequest
        {
            DisplayName = "Invalid client",
            AuthFlow = (OidcClientAuthFlow)999,
            ConsentType = (OidcClientConsentType)999,
            GrantTypes = ["client_credentials"],
        });

        Assert.Equal(["AuthFlow is invalid.", "ConsentType is invalid."], evaluation.Errors);
    }

    [Fact]
    public void Evaluate_WhenUrisAreInvalid_ReturnsFirstErrorForEachUriCollection()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var evaluation = profile.Evaluate(new OidcClientUpsertRequest
        {
            DisplayName = "Interactive client",
            AuthFlow = OidcClientAuthFlow.AuthorizationCode,
            PublicClient = true,
            RequireProofKeyForCodeExchange = true,
            ConsentType = OidcClientConsentType.Implicit,
            GrantTypes = ["authorization_code"],
            RedirectUris = ["not-a-uri"],
            PostLogoutRedirectUris = ["also-not-a-uri"],
        });

        Assert.Equal(
        [
            "Invalid URI: 'not-a-uri'.",
            "Invalid URI: 'also-not-a-uri'.",
        ], evaluation.Errors);
    }

    [Fact]
    public void Evaluate_WhenUriPathsDifferOnlyByCase_PreservesBothUris()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var evaluation = profile.Evaluate(new OidcClientUpsertRequest
        {
            DisplayName = "Interactive client",
            AuthFlow = OidcClientAuthFlow.AuthorizationCode,
            PublicClient = true,
            RequireProofKeyForCodeExchange = true,
            ConsentType = OidcClientConsentType.Implicit,
            GrantTypes = ["authorization_code"],
            RedirectUris =
            [
                "https://client.example.com/Callback",
                "https://client.example.com/callback",
            ],
            PostLogoutRedirectUris =
            [
                "https://client.example.com/Signout",
                "https://client.example.com/signout",
            ],
        });

        Assert.Empty(evaluation.Errors);
        Assert.Equal(2, evaluation.Request.RedirectUris.Length);
        Assert.Equal(2, evaluation.Request.PostLogoutRedirectUris.Length);
    }

    [Fact]
    public void InterpretPersistedProfile_WhenPublicAuthorizationCodeClient_ReturnsInteractiveProfile()
    {
        IOidcClientProfile profile = new OidcClientProfile();

        var interpretation = profile.InterpretPersistedProfile(new OidcClientPersistedProfile
        {
            ClientType = "public",
            ConsentType = "explicit",
            Permissions = ["gt:authorization_code", "gt:refresh_token", "scp:api"],
            RedirectUris = ["https://client.example.com/callback"],
        });

        Assert.Equal(OidcClientAuthFlow.AuthorizationCode, interpretation.AuthFlow);
        Assert.True(interpretation.PublicClient);
        Assert.Equal(OidcClientConsentType.Explicit, interpretation.ConsentType);
        Assert.Equal(["authorization_code", "refresh_token"], interpretation.GrantTypes);
        Assert.Equal(["api"], interpretation.Scopes);
    }
}
