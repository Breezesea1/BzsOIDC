using Bunit;
using BzsOIDC.Idp.Components.Admin;
using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Idp.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace BzsOIDC.Idp.UnitTests.Components.Admin;

public sealed class ClientManagementDialogTests
{
    [Fact]
    public void Render_WhenInteractiveProfile_ShowsRedirectUriEditors()
    {
        using var context = CreateContext();
        var cut = context.Render<ClientManagementDialog>(parameters => parameters
            .Add(x => x.IsOpen, true)
            .Add(x => x.AuthFlow, OidcClientAuthFlow.AuthorizationCode));

        Assert.NotNull(cut.Find("#editor-redirect-uris"));
        Assert.NotNull(cut.Find("#editor-post-logout-uris"));
        Assert.Empty(cut.FindAll("#editor-client-secret"));
    }

    [Fact]
    public void AuthFlowChange_WhenValid_InvokesAuthFlowChanged()
    {
        using var context = CreateContext();
        var changedTo = OidcClientAuthFlow.AuthorizationCode;

        var cut = context.Render<ClientManagementDialog>(parameters => parameters
            .Add(x => x.IsOpen, true)
            .Add(x => x.AuthFlow, OidcClientAuthFlow.AuthorizationCode)
            .Add(x => x.AuthFlowChanged, EventCallback.Factory.Create<OidcClientAuthFlow>(new object(), value => changedTo = value)));

        cut.Find("#editor-auth-flow").Click();
        cut.Find("[data-neo-select-index='1']").Click();

        Assert.Equal(OidcClientAuthFlow.ClientCredentials, changedTo);
    }

    [Fact]
    public void Render_WhenEditing_DisablesClientIdAndUsesEditSaveLabel()
    {
        using var context = CreateContext();
        var cut = context.Render<ClientManagementDialog>(parameters => parameters
            .Add(x => x.IsOpen, true)
            .Add(x => x.IsEditing, true)
            .Add(x => x.AuthFlow, OidcClientAuthFlow.ClientCredentials));

        Assert.NotNull(cut.Find("#editor-client-id[disabled]"));
        Assert.Contains("SaveChanges", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#editor-client-secret"));
    }

    [Fact]
    public void Render_WhenMachineProfile_ShowsPermissionsFromClientProfile()
    {
        using var context = CreateContext();
        var cut = context.Render<ClientManagementDialog>(parameters => parameters
            .Add(x => x.IsOpen, true)
            .Add(x => x.AuthFlow, OidcClientAuthFlow.ClientCredentials)
            .Add(x => x.DisplayName, "Machine client")
            .Add(x => x.ScopesText, "api"));

        Assert.Contains("ept:token", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("ept:introspection", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("gt:client_credentials", cut.Markup, StringComparison.Ordinal);
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IOidcClientProfile, OidcClientProfile>();
        context.Services.AddSingleton<IStringLocalizer<ClientManagement>, TestStringLocalizer<ClientManagement>>();
        return context;
    }
}
