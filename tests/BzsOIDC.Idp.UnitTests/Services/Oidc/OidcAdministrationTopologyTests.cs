using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Services.Oidc;

public sealed class OidcAdministrationTopologyTests
{
    [Fact]
    public async Task ReadAsync_WhenSourcesContainRelationships_ComposesMinimalTopologyInSourceOrder()
    {
        var calls = new List<string>();
        var clients = Substitute.For<IOidcClientService>();
        clients.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("clients");
            return Task.FromResult<IReadOnlyList<OidcClientResponse>>(
            [
                new OidcClientResponse
                {
                    ClientId = "client-1",
                    DisplayName = "Zulu client",
                    AuthFlow = OidcClientAuthFlow.AuthorizationCode,
                    Scopes = ["api"],
                },
                new OidcClientResponse
                {
                    ClientId = "client-2",
                    DisplayName = "Alpha client",
                    AuthFlow = OidcClientAuthFlow.ClientCredentials,
                    Scopes = ["API"],
                },
            ]);
        });
        var scopes = Substitute.For<IOidcScopeService>();
        scopes.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("scopes");
            return Task.FromResult<IReadOnlyList<OidcScopeResponse>>(
            [
                new OidcScopeResponse
                {
                    Name = "api",
                    DisplayName = "API",
                    Description = "API scope",
                    Resources = ["resource"],
                },
            ]);
        });
        var permissions = Substitute.For<IPermissionTopology>();
        permissions.GetResourcesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("permissions");
            return Task.FromResult<IReadOnlyList<ProtectedResourceResponse>>(
            [
                new ProtectedResourceResponse
                {
                    Permissions =
                    [
                        new PermissionDefinitionResponse { Name = "users.write", ReleaseScopes = ["API"] },
                        new PermissionDefinitionResponse { Name = "users.read", ReleaseScopes = ["api"] },
                    ],
                },
            ]);
        });
        IOidcAdministrationTopology sut = new OidcAdministrationTopology(clients, scopes, permissions);

        var snapshot = await sut.ReadAsync();

        Assert.Equal(["clients", "scopes", "permissions"], calls);
        Assert.Equal(2, snapshot.PermissionCount);
        Assert.Equal(["client-1", "client-2"], snapshot.Clients.Select(static client => client.ClientId));
        var scope = Assert.Single(snapshot.Scopes);
        Assert.Equal("API scope", scope.Description);
        Assert.Equal(["resource"], scope.Resources);
        Assert.Equal(["Alpha client", "Zulu client"], scope.Clients);
        Assert.Equal(["users.read", "users.write"], scope.Permissions);
    }

    [Fact]
    public async Task ReadAsync_WhenClientDisplayNameMissing_UsesClientIdInScopeRelationship()
    {
        var clients = Substitute.For<IOidcClientService>();
        clients.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OidcClientResponse>>(
            [
                new OidcClientResponse { ClientId = "client-id", Scopes = ["api"] },
            ]));
        var scopes = Substitute.For<IOidcScopeService>();
        scopes.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OidcScopeResponse>>(
            [
                new OidcScopeResponse { Name = "api" },
            ]));
        var permissions = Substitute.For<IPermissionTopology>();
        permissions.GetResourcesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProtectedResourceResponse>>([]));
        IOidcAdministrationTopology sut = new OidcAdministrationTopology(clients, scopes, permissions);

        var snapshot = await sut.ReadAsync();

        Assert.Equal(["client-id"], Assert.Single(snapshot.Scopes).Clients);
    }

    [Fact]
    public async Task ReadAsync_WhenAllSourcesAreEmpty_ReturnsEmptyTopologyAfterReadingSourcesInOrder()
    {
        var calls = new List<string>();
        var clients = Substitute.For<IOidcClientService>();
        clients.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("clients");
            return Task.FromResult<IReadOnlyList<OidcClientResponse>>([]);
        });
        var scopes = Substitute.For<IOidcScopeService>();
        scopes.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("scopes");
            return Task.FromResult<IReadOnlyList<OidcScopeResponse>>([]);
        });
        var permissions = Substitute.For<IPermissionTopology>();
        permissions.GetResourcesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("permissions");
            return Task.FromResult<IReadOnlyList<ProtectedResourceResponse>>([]);
        });
        IOidcAdministrationTopology sut = new OidcAdministrationTopology(clients, scopes, permissions);

        var snapshot = await sut.ReadAsync();

        Assert.Equal(["clients", "scopes", "permissions"], calls);
        Assert.Empty(snapshot.Clients);
        Assert.Empty(snapshot.Scopes);
        Assert.Equal(0, snapshot.PermissionCount);
    }

    [Fact]
    public async Task ReadAsync_WhenClientSourceFails_PropagatesOriginalExceptionWithoutReadingFollowingSources()
    {
        var calls = new List<string>();
        var expectedException = new InvalidOperationException("client source failed");
        var clients = Substitute.For<IOidcClientService>();
        clients.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("clients");
            return Task.FromException<IReadOnlyList<OidcClientResponse>>(expectedException);
        });
        var scopes = Substitute.For<IOidcScopeService>();
        var permissions = Substitute.For<IPermissionTopology>();
        IOidcAdministrationTopology sut = new OidcAdministrationTopology(clients, scopes, permissions);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ReadAsync());

        Assert.Same(expectedException, exception);
        Assert.Equal(["clients"], calls);
        await scopes.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
        await permissions.DidNotReceive().GetResourcesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_WhenScopeSourceFails_PropagatesOriginalExceptionWithoutReadingPermissionSource()
    {
        var calls = new List<string>();
        var expectedException = new InvalidOperationException("scope source failed");
        var clients = Substitute.For<IOidcClientService>();
        clients.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("clients");
            return Task.FromResult<IReadOnlyList<OidcClientResponse>>([]);
        });
        var scopes = Substitute.For<IOidcScopeService>();
        scopes.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("scopes");
            return Task.FromException<IReadOnlyList<OidcScopeResponse>>(expectedException);
        });
        var permissions = Substitute.For<IPermissionTopology>();
        IOidcAdministrationTopology sut = new OidcAdministrationTopology(clients, scopes, permissions);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ReadAsync());

        Assert.Same(expectedException, exception);
        Assert.Equal(["clients", "scopes"], calls);
        await permissions.DidNotReceive().GetResourcesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_WhenPermissionSourceFails_PropagatesOriginalExceptionAfterReadingPriorSources()
    {
        var calls = new List<string>();
        var expectedException = new InvalidOperationException("permission source failed");
        var clients = Substitute.For<IOidcClientService>();
        clients.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("clients");
            return Task.FromResult<IReadOnlyList<OidcClientResponse>>([]);
        });
        var scopes = Substitute.For<IOidcScopeService>();
        scopes.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("scopes");
            return Task.FromResult<IReadOnlyList<OidcScopeResponse>>([]);
        });
        var permissions = Substitute.For<IPermissionTopology>();
        permissions.GetResourcesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("permissions");
            return Task.FromException<IReadOnlyList<ProtectedResourceResponse>>(expectedException);
        });
        IOidcAdministrationTopology sut = new OidcAdministrationTopology(clients, scopes, permissions);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ReadAsync());

        Assert.Same(expectedException, exception);
        Assert.Equal(["clients", "scopes", "permissions"], calls);
    }
}
