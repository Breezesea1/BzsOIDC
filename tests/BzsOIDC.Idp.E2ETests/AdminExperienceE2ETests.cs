using System.Text.RegularExpressions;
using BzsOIDC.Idp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace BzsOIDC.Idp.E2ETests;

[Collection(E2ETestCollection.Name)]
public sealed class AdminExperienceE2ETests(AppHostFixture fixture) : E2EPageTest
{
    [Fact]
    [Trait("Category", "Smoke")]
    public async Task DashboardAndAdminPages_RenderAfterAdminLogin()
    {
        await AppUi.LoginAsAdminAsync(this, fixture);

        await Expect(Page.Locator("a.sidebar-nav-item[href='/admin/users']")).ToBeVisibleAsync();
        await Expect(Page.Locator("a.sidebar-nav-item[href='/admin/clients']")).ToBeVisibleAsync();

        await Page.GotoAsync(fixture.BuildUrl("/admin/users"));
        await Page.Locator("[data-testid='users-table']").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        await Page.GotoAsync(fixture.BuildUrl("/admin/clients"));
        await Page.Locator("[data-testid='clients-table']").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        await Page.GotoAsync(fixture.BuildUrl("/admin/scopes"));
        await Page.Locator("[data-testid='scopes-table']").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
    }

    [Fact]
    public async Task UserManagement_AllowsCreateEditAndDeleteUser()
    {
        var userName = $"e2e-user-{Guid.NewGuid():N}"[..17];
        var updatedEmail = $"{userName}@example.com";

        await AppUi.LoginAsAdminAsync(this, fixture, "/admin/users");
        await Page.GotoAsync(fixture.BuildUrl("/admin/users"));

        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("新建用户|Create user|Create", RegexOptions.IgnoreCase) }).ClickAsync();
        await Page.Locator("#editor-user-name").FillAsync(userName);
        await Page.Locator("#editor-email").FillAsync($"seed-{Guid.NewGuid():N}@example.com");
        await Page.Locator("#editor-password").FillAsync("Passw0rd!");
        await Page.Locator(".admin-dialog-shell .admin-primary-button").ClickAsync();

        var createdRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = userName });
        await Expect(createdRow).ToBeVisibleAsync(new() { Timeout = 20000 });

        await createdRow.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("编辑|Edit", RegexOptions.IgnoreCase) }).ClickAsync();
        await Page.Locator("#editor-email").FillAsync(updatedEmail);
        await Page.Locator(".admin-dialog-shell .admin-primary-button").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = updatedEmail })).ToBeVisibleAsync(new() { Timeout = 20000 });

        var updatedRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = userName });
        await updatedRow.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("删除|Delete", RegexOptions.IgnoreCase) }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = userName })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ClientManagement_AllowsCreateEditAndDeleteMachineClient()
    {
        var clientId = $"e2e-client-{Guid.NewGuid():N}"[..20];
        var displayName = $"Display {Guid.NewGuid():N}"[..16];
        var updatedDisplayName = $"Updated {Guid.NewGuid():N}"[..16];

        await AppUi.LoginAsAdminAsync(this, fixture, "/admin/clients");
        await Page.GotoAsync(fixture.BuildUrl("/admin/clients"));

        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("新建客户端|Register client|Create", RegexOptions.IgnoreCase) }).ClickAsync();
        await Page.Locator("#editor-client-id").FillAsync(clientId);
        await Page.Locator("#editor-display-name").FillAsync(displayName);
        await Page.Locator("#editor-auth-flow").ClickAsync();
        await Page.Locator("#editor-auth-flow-option-1").ClickAsync();
        await Page.Locator("#editor-scopes").FillAsync("api");
        await Page.Locator("#editor-client-secret").FillAsync("machine-client-secret");
        await Page.Locator(".admin-dialog-shell .admin-primary-button").ClickAsync();

        var createdRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = clientId });
        await Expect(createdRow).ToBeVisibleAsync(new() { Timeout = 20000 });

        await createdRow.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("编辑|Edit", RegexOptions.IgnoreCase) }).ClickAsync();
        await Page.Locator("#editor-display-name").FillAsync(updatedDisplayName);
        await Page.Locator(".admin-dialog-shell .admin-primary-button").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = updatedDisplayName })).ToBeVisibleAsync(new() { Timeout = 20000 });

        var updatedRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = clientId });
        await updatedRow.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("删除|Delete", RegexOptions.IgnoreCase) }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = clientId })).ToHaveCountAsync(0);
    }

}
