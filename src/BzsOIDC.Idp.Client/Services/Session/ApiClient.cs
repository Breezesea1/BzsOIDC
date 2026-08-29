using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BzsOIDC.Contracts;

namespace BzsOIDC.Idp.Client.Services.Session;

public sealed class ApiProblemException(ApiProblemDetails problem)
    : HttpRequestException(problem.Code, inner: null, statusCode: (HttpStatusCode)problem.Status)
{
    public ApiProblemDetails Problem { get; } = problem;
}

public static class ApiProblemDetailsMapper
{
    public static async Task<ApiProblemDetails?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class ApiRequestHandler(
    IAntiforgeryTokenStore antiforgery,
    SessionLifecycle lifecycle) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var canRetry = IsWrite(request.Method);
        var retried = false;
        var contentBytes = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            using var attempt = CloneRequest(request, contentBytes);
            if (canRetry || IsWrite(request.Method))
            {
                var token = await antiforgery.GetOrRefreshAsync(cancellationToken).ConfigureAwait(false);
                attempt.Headers.Remove(token.HeaderName);
                attempt.Headers.TryAddWithoutValidation(token.HeaderName, token.Token);
            }

            var response = await base.SendAsync(attempt, cancellationToken).ConfigureAwait(false);
            var problem = await ApiProblemDetailsMapper.ReadAsync(response, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await lifecycle.HandleUnauthorizedAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // Keep the current session intact while directing the foreground UI.
                lifecycle.NavigateDenied();
            }

            if (!retried && canRetry && problem?.Code == ApiErrorCodes.AntiforgeryFailed)
            {
                retried = true;
                response.Dispose();
                antiforgery.Clear();
                continue;
            }

            return response;
        }
    }

    private static bool IsWrite(HttpMethod method) => method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options;

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source, byte[]? contentBytes)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (contentBytes is not null)
        {
            clone.Content = new ByteArrayContent(contentBytes);
            if (source.Content is not null)
            {
                foreach (var header in source.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }
        return clone;
    }
}
