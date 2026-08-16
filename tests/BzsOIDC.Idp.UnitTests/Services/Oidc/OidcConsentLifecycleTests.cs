using System.Collections.Immutable;
using System.Security.Claims;
using BzsOIDC.Idp.Services.Oidc;
using NSubstitute;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.UnitTests.Services.Oidc;

public sealed class OidcConsentLifecycleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EvaluateAsync_WhenClientIdMissing_ReturnsAuthorizedWithoutApplicationLookup(string? clientId)
    {
        var applications = Substitute.For<IOpenIddictApplicationManager>();
        var sut = new OidcConsentLifecycle(applications, Substitute.For<IOpenIddictAuthorizationManager>());

        var result = await sut.EvaluateAsync(CreateRequest(clientId));

        Assert.Equal(OidcConsentOutcome.Authorized, result.Outcome);
        Assert.Null(result.AuthorizationId);
        await applications.DidNotReceive().FindByClientIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_WhenApplicationMissing_ReturnsAuthorized()
    {
        var applications = Substitute.For<IOpenIddictApplicationManager>();
        applications.FindByClientIdAsync("client", Arg.Any<CancellationToken>()).Returns(new ValueTask<object?>((object?)null));
        var sut = new OidcConsentLifecycle(applications, Substitute.For<IOpenIddictAuthorizationManager>());

        var result = await sut.EvaluateAsync(CreateRequest());

        Assert.Equal(OidcConsentOutcome.Authorized, result.Outcome);
    }

    [Fact]
    public async Task EvaluateAsync_WhenPromptConsentRequested_ReturnsConsentPageRequiredBeforeConsentTypeLookup()
    {
        var application = new object();
        var applications = Substitute.For<IOpenIddictApplicationManager>();
        applications.FindByClientIdAsync("client", Arg.Any<CancellationToken>()).Returns(new ValueTask<object?>(application));
        var sut = new OidcConsentLifecycle(applications, Substitute.For<IOpenIddictAuthorizationManager>());

        var result = await sut.EvaluateAsync(CreateRequest(promptConsent: true));

        Assert.Equal(OidcConsentOutcome.ConsentPageRequired, result.Outcome);
        await applications.DidNotReceive().GetConsentTypeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_WhenConsentIsNotExplicit_ReturnsAuthorized()
    {
        var application = new object();
        var applications = CreateApplications(application, OpenIddictConstants.ConsentTypes.Implicit, "application-id");
        var sut = new OidcConsentLifecycle(applications, Substitute.For<IOpenIddictAuthorizationManager>());

        var result = await sut.EvaluateAsync(CreateRequest());

        Assert.Equal(OidcConsentOutcome.Authorized, result.Outcome);
        await applications.DidNotReceive().GetIdAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_WhenExplicitApplicationHasNoId_ReturnsAuthorizedWithoutAuthorizationLookup()
    {
        var applications = CreateApplications(new object(), OpenIddictConstants.ConsentTypes.Explicit, string.Empty);
        var authorizations = Substitute.For<IOpenIddictAuthorizationManager>();
        var sut = new OidcConsentLifecycle(applications, authorizations);

        var result = await sut.EvaluateAsync(CreateRequest());

        Assert.Equal(OidcConsentOutcome.Authorized, result.Outcome);
        _ = authorizations.DidNotReceive().FindAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ImmutableArray<string>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_WhenMatchingPermanentValidAuthorizationExists_ReturnsItsIdAndUsesRequestedScopes()
    {
        var application = new object();
        var authorization = new object();
        var applications = CreateApplications(application, OpenIddictConstants.ConsentTypes.Explicit, "application-id");
        var authorizations = Substitute.For<IOpenIddictAuthorizationManager>();
        authorizations.FindAsync(
                "user-id",
                "application-id",
                OpenIddictConstants.Statuses.Valid,
                OpenIddictConstants.AuthorizationTypes.Permanent,
                Arg.Any<ImmutableArray<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Authorizations(authorization));
        authorizations.GetIdAsync(authorization, Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("authorization-id"));
        var sut = new OidcConsentLifecycle(applications, authorizations);

        var result = await sut.EvaluateAsync(CreateRequest(scopes: ["openid", "api"]));

        Assert.Equal(OidcConsentOutcome.Authorized, result.Outcome);
        Assert.Equal("authorization-id", result.AuthorizationId);
        _ = authorizations.Received(1).FindAsync(
            "user-id",
            "application-id",
            OpenIddictConstants.Statuses.Valid,
            OpenIddictConstants.AuthorizationTypes.Permanent,
            Arg.Is<ImmutableArray<string>?>(requestedScopes =>
                requestedScopes.HasValue &&
                requestedScopes.Value.SequenceEqual(new[] { "openid", "api" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_WhenPromptNoneHasNoMatchingAuthorization_ReturnsConsentRequired()
    {
        var applications = CreateApplications(new object(), OpenIddictConstants.ConsentTypes.Explicit, "application-id");
        var authorizations = Substitute.For<IOpenIddictAuthorizationManager>();
        authorizations.FindAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ImmutableArray<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Authorizations());
        var sut = new OidcConsentLifecycle(applications, authorizations);

        var result = await sut.EvaluateAsync(CreateRequest(promptNone: true));

        Assert.Equal(OidcConsentOutcome.ConsentRequired, result.Outcome);
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoMatchingAuthorization_ReturnsConsentPageRequired()
    {
        var applications = CreateApplications(new object(), OpenIddictConstants.ConsentTypes.Explicit, "application-id");
        var authorizations = Substitute.For<IOpenIddictAuthorizationManager>();
        authorizations.FindAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ImmutableArray<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Authorizations());
        var sut = new OidcConsentLifecycle(applications, authorizations);

        var result = await sut.EvaluateAsync(CreateRequest());

        Assert.Equal(OidcConsentOutcome.ConsentPageRequired, result.Outcome);
    }

    [Fact]
    public async Task CreatePermanentAuthorizationAsync_WhenApplicationValid_CreatesExpectedDescriptorAndReturnsId()
    {
        var application = new object();
        var createdAuthorization = new object();
        var applications = CreateApplications(application, OpenIddictConstants.ConsentTypes.Explicit, "application-id");
        var authorizations = Substitute.For<IOpenIddictAuthorizationManager>();
        authorizations.CreateAsync(Arg.Any<OpenIddictAuthorizationDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<object>(createdAuthorization));
        authorizations.GetIdAsync(createdAuthorization, Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("authorization-id"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var sut = new OidcConsentLifecycle(applications, authorizations);

        var result = await sut.CreatePermanentAuthorizationAsync(new OidcConsentAuthorizationRequest(
            "client", "user-id", ["openid", "api"], principal));

        Assert.Equal(OidcConsentOutcome.Authorized, result.Outcome);
        Assert.Equal("authorization-id", result.AuthorizationId);
        Assert.Empty(principal.Claims);
        await authorizations.Received(1).CreateAsync(
            Arg.Is<OpenIddictAuthorizationDescriptor>(descriptor =>
                descriptor.ApplicationId == "application-id" &&
                descriptor.Principal == principal &&
                descriptor.Status == OpenIddictConstants.Statuses.Valid &&
                descriptor.Subject == "user-id" &&
                descriptor.Type == OpenIddictConstants.AuthorizationTypes.Permanent &&
                descriptor.Scopes.SetEquals(new[] { "openid", "api" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePermanentAuthorizationAsync_WhenApplicationIsMissing_ThrowsCurrentApprovalError()
    {
        var applications = Substitute.For<IOpenIddictApplicationManager>();
        applications.FindByClientIdAsync("client", Arg.Any<CancellationToken>()).Returns(new ValueTask<object?>((object?)null));
        var sut = new OidcConsentLifecycle(applications, Substitute.For<IOpenIddictAuthorizationManager>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreatePermanentAuthorizationAsync(
            new OidcConsentAuthorizationRequest("client", "user-id", ["openid"], new ClaimsPrincipal())));

        Assert.Equal("An explicit consent authorization requires a valid OIDC application.", exception.Message);
    }

    [Fact]
    public async Task CreatePermanentAuthorizationAsync_WhenCreatedAuthorizationHasNoId_ReturnsOptionalEmptyId()
    {
        var application = new object();
        var createdAuthorization = new object();
        var applications = CreateApplications(application, OpenIddictConstants.ConsentTypes.Explicit, "application-id");
        var authorizations = Substitute.For<IOpenIddictAuthorizationManager>();
        authorizations.CreateAsync(Arg.Any<OpenIddictAuthorizationDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<object>(createdAuthorization));
        authorizations.GetIdAsync(createdAuthorization, Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>(string.Empty));
        var sut = new OidcConsentLifecycle(applications, authorizations);

        var result = await sut.CreatePermanentAuthorizationAsync(new OidcConsentAuthorizationRequest(
            "client", "user-id", ["openid"], new ClaimsPrincipal()));

        Assert.Equal(OidcConsentOutcome.Authorized, result.Outcome);
        Assert.Null(result.AuthorizationId);
    }

    [Fact]
    public async Task CreatePermanentAuthorizationAsync_WhenAuthorizationCreationFails_PropagatesOriginalException()
    {
        var application = new object();
        var expectedException = new InvalidOperationException("authorization creation failed");
        var applications = CreateApplications(application, OpenIddictConstants.ConsentTypes.Explicit, "application-id");
        var authorizations = Substitute.For<IOpenIddictAuthorizationManager>();
        authorizations.CreateAsync(Arg.Any<OpenIddictAuthorizationDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<object>(Task.FromException<object>(expectedException)));
        var sut = new OidcConsentLifecycle(applications, authorizations);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreatePermanentAuthorizationAsync(
            new OidcConsentAuthorizationRequest("client", "user-id", ["openid"], new ClaimsPrincipal())));

        Assert.Same(expectedException, exception);
        await authorizations.DidNotReceive().GetIdAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    private static OidcConsentRequest CreateRequest(
        string? clientId = "client",
        IReadOnlyList<string>? scopes = null,
        bool promptConsent = false,
        bool promptNone = false)
    {
        return new OidcConsentRequest(clientId, "user-id", scopes ?? ["openid"], promptConsent, promptNone);
    }

    private static IOpenIddictApplicationManager CreateApplications(object application, string consentType, string applicationId)
    {
        var applications = Substitute.For<IOpenIddictApplicationManager>();
        applications.FindByClientIdAsync("client", Arg.Any<CancellationToken>()).Returns(new ValueTask<object?>(application));
        applications.GetConsentTypeAsync(application, Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>(consentType));
        applications.GetIdAsync(application, Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>(applicationId));
        return applications;
    }

    private static async IAsyncEnumerable<object> Authorizations(params object[] authorizations)
    {
        foreach (var authorization in authorizations)
        {
            yield return authorization;
        }

        await Task.CompletedTask;
    }
}
