using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using BzsOIDC.Contracts;
using Microsoft.AspNetCore.Components.Authorization;

namespace BzsOIDC.Idp.Client.Services.Session;

public interface ISessionClient
{
    Task<SessionSummary> GetSessionAsync(CancellationToken cancellationToken = default);
}

public sealed class SessionClient(HttpClient httpClient) : ISessionClient
{
    public async Task<SessionSummary> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/session", cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new SessionSummary();
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionSummary>(cancellationToken: cancellationToken)
            ?? new SessionSummary();
    }
}

public sealed class SessionAuthenticationStateProvider(ISessionClient sessionClient)
    : AuthenticationStateProvider, IAsyncDisposable
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private SessionSummary _summary = new();
    private bool _initialized;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await RefreshAsync().ConfigureAwait(false);
        return CreateState(_summary);
    }

    public async Task<SessionSummary> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = await sessionClient.GetSessionAsync(cancellationToken).ConfigureAwait(false);
            if (!_initialized || !AreEqual(_summary, next))
            {
                _summary = next;
                _initialized = true;
                NotifyAuthenticationStateChanged(Task.FromResult(CreateState(next)));
            }

            return _summary;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Invalidate()
    {
        if (!_summary.IsAuthenticated)
        {
            return;
        }

        _summary = new SessionSummary();
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(Anonymous)));
    }

    private static AuthenticationState CreateState(SessionSummary summary)
    {
        if (!summary.IsAuthenticated)
        {
            return new AuthenticationState(Anonymous);
        }

        var identity = new ClaimsIdentity("bzs-session", ClaimTypes.Name, ClaimTypes.Role);
        if (!string.IsNullOrWhiteSpace(summary.UserId))
        {
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, summary.UserId));
        }
        if (!string.IsNullOrWhiteSpace(summary.UserName))
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, summary.UserName));
        }
        if (!string.IsNullOrWhiteSpace(summary.DisplayName))
        {
            identity.AddClaim(new Claim("display_name", summary.DisplayName));
        }
        foreach (var role in summary.Roles)
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
        }
        foreach (var permission in summary.Permissions)
        {
            identity.AddClaim(new Claim("permission", permission));
        }

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    private static bool AreEqual(SessionSummary left, SessionSummary right) =>
        left.IsAuthenticated == right.IsAuthenticated &&
        string.Equals(left.UserId, right.UserId, StringComparison.Ordinal) &&
        string.Equals(left.UserName, right.UserName, StringComparison.Ordinal) &&
        string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
        left.Roles.SequenceEqual(right.Roles, StringComparer.OrdinalIgnoreCase) &&
        left.Permissions.SequenceEqual(right.Permissions, StringComparer.OrdinalIgnoreCase);

    public ValueTask DisposeAsync()
    {
        _refreshGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
