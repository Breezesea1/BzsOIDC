using System.Security.Claims;
using BootstrapBlazor.Components;
using Bunit;
using Bunit.JSInterop;
using BzsOIDC.Idp.Components.Admin;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Components.Admin;

public sealed class UserManagementPageTests
{
    [Fact]
    public void Search_WhenTriggeredFromLaterPage_ResetsToFirstPageOfMatches()
    {
        using var context = CreateContext();

        var users = Enumerable.Range(1, 15)
            .Select(index => new BzsUser
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
                UserName = $"alpha-user-{index:00}",
                Email = $"alpha{index:00}@example.com"
            })
            .Concat(Enumerable.Range(16, 10).Select(index => new BzsUser
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
                UserName = $"beta-user-{index:00}",
                Email = $"beta{index:00}@example.com"
            }))
            .ToArray();

        var userService = Substitute.For<IUserService>();
        userService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BzsUser>>(users));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton(userService);
        context.Services.AddSingleton<UserManager<BzsUser>>(new TestUserManager());
        context.Services.AddSingleton<IStringLocalizer<UserManagement>, TestStringLocalizer<UserManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());

        var cut = context.Render<UserManagement>();

        cut.WaitForAssertion(() => Assert.Equal(10, cut.FindAll(".admin-bootstrap-table .table-card > .table-row").Count));

        cut.FindAll("button").Single(button => button.TextContent.Contains("NextPage", StringComparison.Ordinal)).Click();
        cut.FindAll("button").Single(button => button.TextContent.Contains("NextPage", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Contains("beta-user-21", cut.Markup, StringComparison.Ordinal));

        cut.Find("#user-search").Input("alpha");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("alpha-user-01", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("alpha-user-11", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(10, cut.FindAll(".admin-bootstrap-table .table-card > .table-row").Count);
        });
    }


    [Fact]
    public void Toolbar_RendersControlsInSingleCommandBarWithoutHeroCopy()
    {
        using var context = CreateContext();

        var users = CreateUsers();
        var userService = Substitute.For<IUserService>();
        userService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BzsUser>>(users));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton(userService);
        context.Services.AddSingleton<UserManager<BzsUser>>(new TestUserManager((user, role) => (user.UserName ?? string.Empty).StartsWith("admin-", StringComparison.OrdinalIgnoreCase)));
        context.Services.AddSingleton<IStringLocalizer<UserManagement>, TestStringLocalizer<UserManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());

        var cut = context.Render<UserManagement>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".admin-users-command-bar")));

        Assert.Empty(cut.FindAll(".admin-hero"));
        Assert.Empty(cut.FindAll(".admin-breadcrumb"));

        var commandBar = cut.Find(".admin-users-command-bar");
        Assert.NotNull(commandBar.QuerySelector(".admin-users-filter-tabs"));
        Assert.NotNull(commandBar.QuerySelector("#user-search"));
        Assert.Contains("Refresh", commandBar.TextContent, StringComparison.Ordinal);
        Assert.Contains("NewUser", commandBar.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void RoleFilter_WhenToggled_FiltersUsersAndPreservesCounts()
    {
        using var context = CreateContext();

        var users = CreateUsers();
        var userService = Substitute.For<IUserService>();
        userService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BzsUser>>(users));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton(userService);
        context.Services.AddSingleton<UserManager<BzsUser>>(new TestUserManager((user, role) => (user.UserName ?? string.Empty).StartsWith("admin-", StringComparison.OrdinalIgnoreCase)));
        context.Services.AddSingleton<IStringLocalizer<UserManagement>, TestStringLocalizer<UserManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());

        var cut = context.Render<UserManagement>();

        cut.WaitForAssertion(() => Assert.Contains("admin-alpha", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button.admin-filter-tab").Single(button => button.TextContent.Contains("Admin", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("admin-alpha", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("beta-user", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button.admin-filter-tab").Single(button => button.TextContent.Contains("Standard", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("beta-user", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("admin-alpha", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Search_WhenRoleKeywordEntered_FindsUsersByRoleText()
    {
        using var context = CreateContext();

        var users = CreateUsers();
        var userService = Substitute.For<IUserService>();
        userService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BzsUser>>(users));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton(userService);
        context.Services.AddSingleton<UserManager<BzsUser>>(new TestUserManager((user, role) => (user.UserName ?? string.Empty).StartsWith("admin-", StringComparison.OrdinalIgnoreCase)));
        context.Services.AddSingleton<IStringLocalizer<UserManagement>, TestStringLocalizer<UserManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());

        var cut = context.Render<UserManagement>();

        cut.WaitForAssertion(() => Assert.Contains("beta-user", cut.Markup, StringComparison.Ordinal));

        cut.Find("#user-search").Input("administrator");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("admin-alpha", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("beta-user", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task QueryUsersAsync_WhenBootstrapBlazorColumnFilterProvided_FiltersReturnedRows()
    {
        using var context = CreateContext();

        var users = CreateUsers();
        var userService = Substitute.For<IUserService>();
        userService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BzsUser>>(users));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton(userService);
        context.Services.AddSingleton<UserManager<BzsUser>>(new TestUserManager((user, role) => false));
        context.Services.AddSingleton<IStringLocalizer<UserManagement>, TestStringLocalizer<UserManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());

        var cut = context.Render<UserManagement>();
        cut.WaitForAssertion(() => Assert.Contains("beta-user", cut.Markup, StringComparison.Ordinal));

        var options = new QueryPageOptions
        {
            PageIndex = 1,
            PageItems = 10,
        };
        options.Filters.Add(new SearchFilterAction(nameof(BzsUser.Email), "beta", FilterAction.Contains));

        var queryMethod = typeof(UserManagement).GetMethod(
            "QueryUsersAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(queryMethod);

        var result = await cut.InvokeAsync(async () =>
        {
            var task = (Task)queryMethod!.Invoke(cut.Instance, [options])!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        });

        var items = (System.Collections.IEnumerable)result.GetType().GetProperty("Items")!.GetValue(result)!;
        var userNames = items
            .Cast<object>()
            .Select(item => item.GetType().GetProperty("UserName")!.GetValue(item)?.ToString() ?? string.Empty)
            .ToArray();

        Assert.Equal(["beta-user"], userNames);
    }

    [Fact]
    public void UserTable_WhenRowCheckboxClicked_ShowsSelectionSummaryWithoutDuplicateCheckboxes()
    {
        using var context = CreateContext();

        var users = CreateUsers();
        var userService = Substitute.For<IUserService>();
        userService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BzsUser>>(users));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton(userService);
        context.Services.AddSingleton<UserManager<BzsUser>>(new TestUserManager((user, role) => (user.UserName ?? string.Empty).StartsWith("admin-", StringComparison.OrdinalIgnoreCase)));
        context.Services.AddSingleton<IStringLocalizer<UserManagement>, TestStringLocalizer<UserManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());

        var cut = context.Render<UserManagement>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='users-table'] .table-card > .table-row")));

        Assert.Empty(cut.FindAll("[data-testid='users-batch-actions']"));
        Assert.Empty(cut.FindAll("[data-testid='users-row-select']"));

        var rowCount = cut.FindAll("[data-testid='users-table'] .table-card > .table-row").Count;
        var checkboxes = cut.FindAll("[data-testid='users-table'] input[type='checkbox']");

        Assert.InRange(checkboxes.Count, 1, rowCount + 1);
        checkboxes.Last().Click();

        cut.WaitForAssertion(() =>
        {
            var summary = cut.Find("[data-testid='users-selection-summary']");
            Assert.Contains("SelectedRows", summary.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DeleteAction_WhenCurrentUserRowIsRendered_DisablesSelfDelete()
    {
        using var context = CreateContext();

        var currentUserId = Guid.NewGuid();
        var currentUser = new BzsUser
        {
            Id = currentUserId,
            UserName = "current-admin",
            Email = "current@example.com"
        };

        var otherUser = new BzsUser
        {
            Id = Guid.NewGuid(),
            UserName = "other-user",
            Email = "other@example.com"
        };

        var users = new[] { currentUser, otherUser };
        var userService = Substitute.For<IUserService>();
        userService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BzsUser>>(users));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton(userService);
        context.Services.AddSingleton<UserManager<BzsUser>>(new TestUserManager((user, role) => true));
        context.Services.AddSingleton<IStringLocalizer<UserManagement>, TestStringLocalizer<UserManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor(currentUserId));

        var cut = context.Render<UserManagement>();

        cut.WaitForAssertion(() => Assert.Contains("current-admin", cut.Markup, StringComparison.Ordinal));

        var currentRow = cut.FindAll(".admin-bootstrap-table .table-card > .table-row").Single(row => row.TextContent.Contains("current-admin", StringComparison.Ordinal));
        var deleteButton = currentRow.QuerySelector("button[aria-label='Delete']");
        Assert.NotNull(deleteButton);

        Assert.True(deleteButton!.HasAttribute("disabled"));
    }

    [Fact]
    public void RowActions_WhenUserRowIsRendered_ExposeEditAndDeleteActions()
    {
        using var context = CreateContext();

        var users = CreateUsers();
        var userService = Substitute.For<IUserService>();
        userService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BzsUser>>(users));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton(userService);
        context.Services.AddSingleton<UserManager<BzsUser>>(new TestUserManager((user, role) => false));
        context.Services.AddSingleton<IStringLocalizer<UserManagement>, TestStringLocalizer<UserManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());

        var cut = context.Render<UserManagement>();

        cut.WaitForAssertion(() => Assert.Contains("beta-user", cut.Markup, StringComparison.Ordinal));

        var betaRow = cut.FindAll("[data-testid='users-table'] .table-card > .table-row").Single(row => row.TextContent.Contains("beta-user", StringComparison.Ordinal));

        Assert.NotNull(betaRow.QuerySelector("button[aria-label='Edit']"));
        Assert.NotNull(betaRow.QuerySelector("button[aria-label='Delete']"));
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./Components/Admin/AdminDialogShell.razor.js")
            .SetupVoid("activate", _ => true);
        return context;
    }

    private static IReadOnlyList<BzsUser> CreateUsers()
    {
        return
        [
            new BzsUser
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                UserName = "admin-alpha",
                Email = "admin.alpha@example.com",
            },
            new BzsUser
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                UserName = "beta-user",
                Email = "beta@example.com",
            },
            new BzsUser
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                UserName = "gamma-user",
                Email = "gamma@example.com",
            }
        ];
    }

    private static IHttpContextAccessor CreateAdminHttpContextAccessor(Guid? currentUserId = null)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, (currentUserId ?? Guid.NewGuid()).ToString()),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "admin")
            ],
            "TestAuth");

        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }
}
