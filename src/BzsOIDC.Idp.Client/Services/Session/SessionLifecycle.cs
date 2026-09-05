using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BzsOIDC.Idp.Client.Services.Session;

/// <summary>Coordinates refresh triggers without introducing a polling timer.</summary>
public sealed class SessionLifecycle(
    SessionAuthenticationStateProvider session,
    NavigationManager navigation,
    IJSRuntime? js = null)
{
    private readonly SemaphoreSlim _unauthorizedGate = new(1, 1);
    private int _unauthorizedHandled;

    public async Task OnVisibilityChangedAsync(CancellationToken cancellationToken = default)
    {
        await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _unauthorizedHandled, 0);
    }

    public async Task OnProtectedNavigationAsync(CancellationToken cancellationToken = default)
    {
        await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _unauthorizedHandled, 0);
    }

    public async Task HandleUnauthorizedAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _unauthorizedHandled, 1) != 0)
        {
            return;
        }

        if (!await _unauthorizedGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            session.Invalidate();
            await PublishSessionExpiryAsync();
            var uri = new Uri(navigation.Uri, UriKind.Absolute);
            var loginPath = ClientPath("login");
            if (string.Equals(uri.AbsolutePath, loginPath, StringComparison.OrdinalIgnoreCase))
            {
                // A stale session cookie also 401s while already on the login page;
                // force-loading here would reload the same page forever.
                return;
            }

            var returnUrl = uri.PathAndQuery;
            if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/'))
            {
                returnUrl = "/";
            }
            navigation.NavigateTo($"{loginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}", forceLoad: true);
        }
        finally
        {
            _unauthorizedGate.Release();
        }
    }

    public void NavigateDenied() => navigation.NavigateTo(ClientPath("account/denied"), forceLoad: true);

    private string ClientPath(string path)
    {
        var basePath = new Uri(navigation.BaseUri, UriKind.Absolute).AbsolutePath.TrimEnd('/');
        return string.IsNullOrEmpty(basePath) ? $"/{path}" : $"{basePath}/{path}";
    }

    private async Task PublishSessionExpiryAsync()
    {
        if (js is null)
        {
            return;
        }

        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("import", "./session-sync.js");
            await module.InvokeVoidAsync("publishSession", "expired");
            await module.DisposeAsync();
        }
        catch (JSException)
        {
            // Local recovery remains authoritative if browser synchronization is unavailable.
        }
    }
}
