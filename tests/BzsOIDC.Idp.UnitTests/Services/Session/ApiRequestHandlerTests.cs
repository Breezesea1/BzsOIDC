using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BzsOIDC.Contracts;
using BzsOIDC.Idp.Client.Services.Session;
using Microsoft.AspNetCore.Components;

namespace BzsOIDC.Idp.UnitTests.Services.Session;

public sealed class ApiRequestHandlerTests
{
    [Fact]
    public async Task WriteRequest_AddsTokenAndRetriesOnceOnAntiforgeryFailure()
    {
        var tokenStore = new FakeTokenStore();
        var session = new SessionAuthenticationStateProvider(new FakeSessionClient());
        var navigation = new TestNavigationManager();
        var lifecycle = new SessionLifecycle(session, navigation);
        var inner = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new ApiProblemDetails(400, ApiErrorCodes.AntiforgeryFailed, "t"), mediaType: new MediaTypeHeaderValue("application/problem+json"))
            },
            new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new ApiRequestHandler(tokenStore, lifecycle) { InnerHandler = inner };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        using var response = await client.PostAsync("api/test", JsonContent.Create(new { value = 1 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.All(inner.Requests, request => Assert.Equal("token-1", request.Headers.GetValues("RequestVerificationToken").Single()));
        Assert.Equal(2, tokenStore.RefreshCount);
    }

    [Fact]
    public async Task Unauthorized_NavigatesToLoginWithLocalReturn()
    {
        var navigation = new TestNavigationManager();
        var lifecycle = new SessionLifecycle(new SessionAuthenticationStateProvider(new FakeSessionClient()), navigation);
        var handler = new ApiRequestHandler(new FakeTokenStore(), lifecycle)
        {
            InnerHandler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        using var response = await client.GetAsync("api/private");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("https://localhost/login?returnUrl=", navigation.LastNavigation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forbidden_NavigatesToDeniedWithoutInvalidatingSession()
    {
        var navigation = new TestNavigationManager();
        var lifecycle = new SessionLifecycle(new SessionAuthenticationStateProvider(new FakeSessionClient()), navigation);
        var handler = new ApiRequestHandler(new FakeTokenStore(), lifecycle)
        {
            InnerHandler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.Forbidden))
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        using var response = await client.GetAsync("api/private");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("https://localhost/account/denied", navigation.LastNavigation);
    }

    [Fact]
    public async Task Unauthorized_WhenAlreadyOnLogin_DoesNotNavigateAgain()
    {
        var navigation = new TestNavigationManager("https://localhost/login?ReturnUrl=%2Fconnect%2Fauthorize");
        var lifecycle = new SessionLifecycle(new SessionAuthenticationStateProvider(new FakeSessionClient()), navigation);
        var handler = new ApiRequestHandler(new FakeTokenStore(), lifecycle)
        {
            InnerHandler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        using var response = await client.GetAsync("api/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("https://localhost/", navigation.LastNavigation);
    }

    private sealed class FakeTokenStore : IAntiforgeryTokenStore
    {
        public int RefreshCount { get; private set; }
        public string? Token => "token-1";
        public string HeaderName => "RequestVerificationToken";
        public string FormFieldName => "__RequestVerificationToken";
        public Task<AntiforgeryTokenResponse> GetOrRefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.FromResult(new AntiforgeryTokenResponse { Token = Token!, HeaderName = HeaderName, FormFieldName = FormFieldName });
        }
        public void Clear() { }
    }

    private sealed class FakeSessionClient : ISessionClient
    {
        public Task<SessionSummary> GetSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SessionSummary());
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        private int _index;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responses[Math.Min(_index++, responses.Length - 1)]);
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public string LastNavigation { get; private set; } = "https://localhost/";
        public TestNavigationManager(string currentUri = "https://localhost/") => Initialize("https://localhost/", currentUri);
        protected override void NavigateToCore(string uri, NavigationOptions options) => LastNavigation = ToAbsoluteUri(uri).ToString();
    }
}
