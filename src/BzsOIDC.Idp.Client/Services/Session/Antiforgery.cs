using System.Net.Http.Json;
using BzsOIDC.Contracts;

namespace BzsOIDC.Idp.Client.Services.Session;

public interface IAntiforgeryTokenStore
{
    string? Token { get; }
    string HeaderName { get; }
    string FormFieldName { get; }
    Task<AntiforgeryTokenResponse> GetOrRefreshAsync(CancellationToken cancellationToken = default);
    void Clear();
}

public sealed class AntiforgeryTokenStore(HttpClient httpClient) : IAntiforgeryTokenStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private string _headerName = "RequestVerificationToken";
    private string _formFieldName = "__RequestVerificationToken";

    public string? Token => _token;
    public string HeaderName => _headerName;
    public string FormFieldName => _formFieldName;

    public async Task<AntiforgeryTokenResponse> GetOrRefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_token))
        {
            return new AntiforgeryTokenResponse { Token = _token, HeaderName = _headerName, FormFieldName = _formFieldName };
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(_token))
            {
                using var response = await httpClient.GetAsync("/api/security/antiforgery", cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The antiforgery endpoint returned an empty response.");
                _token = result.Token;
                _headerName = result.HeaderName;
                _formFieldName = result.FormFieldName;
            }

            return new AntiforgeryTokenResponse { Token = _token!, HeaderName = _headerName, FormFieldName = _formFieldName };
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Clear() => _token = null;
}
