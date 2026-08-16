using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.Services.Oidc;

public interface IOidcClientService
{
    Task<OidcClientCommandResult<OidcClientRegistrationResponse>> RegisterAsync(
        OidcClientUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OidcClientResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<OidcClientResponse?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default);

    Task<OidcClientCommandResult<OidcClientResponse>> UpdateAsync(
        string clientId,
        OidcClientUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string clientId, CancellationToken cancellationToken = default);
}

internal sealed class OidcClientService(
    IOpenIddictApplicationManager applicationManager,
    IOidcClientProfile clientProfile) : IOidcClientService
{
    /// <summary>
    /// 创建数据。
    /// </summary>
    /// <param name="request">参数request。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    public async Task<OidcClientCommandResult<OidcClientRegistrationResponse>> RegisterAsync(
        OidcClientUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var evaluation = clientProfile.Evaluate(request);
        if (evaluation.Errors.Length > 0)
        {
            return new OidcClientCommandResult<OidcClientRegistrationResponse>
            {
                Status = OidcClientCommandStatus.ValidationFailed,
                Errors = evaluation.Errors,
            };
        }

        var clientId = evaluation.Request.ClientId ?? $"client-{Guid.NewGuid():N}";

        var exists = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (exists is not null)
        {
            return new OidcClientCommandResult<OidcClientRegistrationResponse>
            {
                Status = OidcClientCommandStatus.Conflict,
                Errors = [$"Client '{clientId}' already exists."],
            };
        }

        var descriptor = OidcClientDescriptorAdapter.CreateDescriptor(evaluation, clientId);
        await applicationManager.CreateAsync(descriptor, cancellationToken);

        return new OidcClientCommandResult<OidcClientRegistrationResponse>
        {
            Status = OidcClientCommandStatus.Success,
            Value = new OidcClientRegistrationResponse
            {
                ClientId = descriptor.ClientId!,
                ClientSecret = descriptor.ClientSecret,
                DisplayName = descriptor.DisplayName ?? descriptor.ClientId!,
                AuthFlow = evaluation.AuthFlow!.Value,
            },
        };
    }

    /// <summary>
    /// 获取数据。
    /// </summary>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    public async Task<IReadOnlyList<OidcClientResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<OidcClientResponse>();

        await foreach (var application in applicationManager.ListAsync())
        {
            list.Add(await ToResponseAsync(application, cancellationToken));
        }

        return list.OrderBy(static x => x.ClientId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// 获取数据。
    /// </summary>
    /// <param name="clientId">参数clientId。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    public async Task<OidcClientResponse?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        return application is null ? null : await ToResponseAsync(application, cancellationToken);
    }

    /// <summary>
    /// 更新数据。
    /// </summary>
    /// <param name="clientId">参数clientId。</param>
    /// <param name="request">参数request。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    public async Task<OidcClientCommandResult<OidcClientResponse>> UpdateAsync(
        string clientId,
        OidcClientUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var evaluation = clientProfile.Evaluate(request);
        if (evaluation.Errors.Length > 0)
        {
            return new OidcClientCommandResult<OidcClientResponse>
            {
                Status = OidcClientCommandStatus.ValidationFailed,
                Errors = evaluation.Errors,
            };
        }

        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            return new OidcClientCommandResult<OidcClientResponse>
            {
                Status = OidcClientCommandStatus.NotFound,
            };
        }

        var descriptor = OidcClientDescriptorAdapter.CreateDescriptor(evaluation, clientId);
        await applicationManager.UpdateAsync(application, descriptor, cancellationToken);

        return new OidcClientCommandResult<OidcClientResponse>
        {
            Status = OidcClientCommandStatus.Success,
            Value = await ToResponseAsync(application, cancellationToken),
        };
    }

    /// <summary>
    /// 删除数据。
    /// </summary>
    /// <param name="clientId">参数clientId。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    public async Task<bool> DeleteAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            return false;
        }

        await applicationManager.DeleteAsync(application, cancellationToken);
        return true;
    }

    /// <summary>
    /// 执行ToResponseAsync。
    /// </summary>
    /// <param name="application">参数application。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    private async Task<OidcClientResponse> ToResponseAsync(object application, CancellationToken cancellationToken)
    {
        var permissions = (await applicationManager.GetPermissionsAsync(application, cancellationToken)).ToArray();
        var requirements = (await applicationManager.GetRequirementsAsync(application, cancellationToken)).ToArray();
        var redirectUris = (await applicationManager.GetRedirectUrisAsync(application, cancellationToken)).ToArray();
        var postLogoutRedirectUris = (await applicationManager.GetPostLogoutRedirectUrisAsync(application, cancellationToken)).ToArray();
        var interpretation = clientProfile.InterpretPersistedProfile(new OidcClientPersistedProfile
        {
            ClientType = await applicationManager.GetClientTypeAsync(application, cancellationToken),
            ConsentType = await applicationManager.GetConsentTypeAsync(application, cancellationToken),
            Permissions = permissions,
            RedirectUris = redirectUris,
        });

        return new OidcClientResponse
        {
            ClientId = await applicationManager.GetClientIdAsync(application, cancellationToken) ?? string.Empty,
            DisplayName = await applicationManager.GetDisplayNameAsync(application, cancellationToken),
            AuthFlow = interpretation.AuthFlow,
            PublicClient = interpretation.PublicClient,
            ConsentType = interpretation.ConsentType,
            GrantTypes = interpretation.GrantTypes,
            Scopes = interpretation.Scopes,
            RedirectUris = redirectUris,
            PostLogoutRedirectUris = postLogoutRedirectUris,
            Permissions = permissions,
            Requirements = requirements,
        };
    }

}
