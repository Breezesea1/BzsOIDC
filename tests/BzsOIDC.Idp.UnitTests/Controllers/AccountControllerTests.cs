using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Controllers;

public sealed class AccountControllerTests
{
    [Fact]
    public async Task Login_WhenCredentialsMissing_RedirectsToLoginWithValidationError()
    {
        var signInManager = CreateSignInManager();
        var sut = CreateSut(signInManager, static url => url == "/admin/users");

        var result = await sut.Login(new AccountController.LoginForm(), "/admin/users");

        var redirect = Assert.IsType<RedirectResult>(result);
        var uri = new Uri($"https://localhost{redirect.Url}");
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("/login", uri.AbsolutePath);
        Assert.Equal("validation", query["error"].ToString());
        Assert.Equal("/admin/users", query["returnUrl"].ToString());
        await signInManager.DidNotReceive()
            .PasswordSignInAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Login_WhenCredentialsValid_LocalRedirectsToSafeReturnUrl()
    {
        var signInManager = CreateSignInManager();
        signInManager.PasswordSignInAsync("admin", "admin123", true, true)
            .Returns(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var sut = CreateSut(signInManager, static url => url == "/admin/users");

        var result = await sut.Login(new AccountController.LoginForm
        {
            UserName = "  admin  ",
            Password = "admin123",
            RememberMe = true,
        }, "/admin/users");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/admin/users", redirect.Url);
        await signInManager.Received(1)
            .PasswordSignInAsync("admin", "admin123", true, true);
    }

    [Fact]
    public async Task Login_WhenCredentialsInvalid_RedirectsToLoginWithoutUnsafeReturnUrl()
    {
        var signInManager = CreateSignInManager();
        signInManager.PasswordSignInAsync("admin", "wrong-password", false, true)
            .Returns(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        var sut = CreateSut(signInManager, static _ => false);

        var result = await sut.Login(new AccountController.LoginForm
        {
            UserName = "admin",
            Password = "wrong-password",
        }, "https://evil.example");

        var redirect = Assert.IsType<RedirectResult>(result);
        var uri = new Uri($"https://localhost{redirect.Url}");
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("/login", uri.AbsolutePath);
        Assert.Equal("invalid_credentials", query["error"].ToString());
        Assert.False(query.ContainsKey("returnUrl"));
    }

    [Fact]
    public async Task Register_WhenRequiredFieldsMissing_RedirectsToRegisterWithValidationError()
    {
        var signInManager = CreateSignInManager();
        var userService = Substitute.For<IUserService>();
        var sut = CreateSut(signInManager, userService, static url => url == "/admin/users");

        var result = await sut.Register(new AccountController.RegisterForm(), "/admin/users", CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        var uri = new Uri($"https://localhost{redirect.Url}");
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("/register", uri.AbsolutePath);
        Assert.Equal("validation", query["error"].ToString());
        Assert.Equal("/admin/users", query["returnUrl"].ToString());
        await userService.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_WhenPasswordsDoNotMatch_RedirectsToRegisterWithMismatchError()
    {
        var signInManager = CreateSignInManager();
        var userService = Substitute.For<IUserService>();
        var sut = CreateSut(signInManager, userService, static url => url == "/admin/users");

        var result = await sut.Register(new AccountController.RegisterForm
        {
            UserName = "new-user",
            Email = "new@example.com",
            Password = "Passw0rd!",
            ConfirmPassword = "different",
        }, "/admin/users", CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        var uri = new Uri($"https://localhost{redirect.Url}");
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("/register", uri.AbsolutePath);
        Assert.Equal("password_mismatch", query["error"].ToString());
        await userService.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_WhenCreationSucceeds_SignsInAndRedirectsToSafeReturnUrl()
    {
        var signInManager = CreateSignInManager();
        var userService = Substitute.For<IUserService>();
        userService.CreateAsync("new-user", "Passw0rd!", "new@example.com", CancellationToken.None)
            .Returns(IdentityResult.Success);
        signInManager.PasswordSignInAsync("new-user", "Passw0rd!", false, false)
            .Returns(Microsoft.AspNetCore.Identity.SignInResult.Success);
        var sut = CreateSut(signInManager, userService, static url => url == "/admin/users");

        var result = await sut.Register(new AccountController.RegisterForm
        {
            UserName = "  new-user  ",
            Email = "  new@example.com  ",
            Password = "Passw0rd!",
            ConfirmPassword = "Passw0rd!",
        }, "/admin/users", CancellationToken.None);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/admin/users", redirect.Url);
        await userService.Received(1).CreateAsync("new-user", "Passw0rd!", "new@example.com", CancellationToken.None);
        await signInManager.Received(1).PasswordSignInAsync("new-user", "Passw0rd!", false, false);
    }

    [Fact]
    public void Logout_WhenReturnUrlSafe_SignsOutToLocalTarget()
    {
        var sut = CreateSut(CreateSignInManager(), static url => url == "/admin/users");

        var result = sut.Logout("/admin/users");

        var signOut = Assert.IsType<SignOutResult>(result);
        Assert.Equal([IdentityConstants.ApplicationScheme], signOut.AuthenticationSchemes);
        Assert.Equal("/admin/users", signOut.Properties?.RedirectUri);
    }

    [Fact]
    public void Logout_WhenReturnUrlUnsafe_SignsOutToRoot()
    {
        var sut = CreateSut(CreateSignInManager(), static _ => false);

        var result = sut.Logout("https://evil.example");

        var signOut = Assert.IsType<SignOutResult>(result);
        Assert.Equal([IdentityConstants.ApplicationScheme], signOut.AuthenticationSchemes);
        Assert.Equal("/", signOut.Properties?.RedirectUri);
    }

    [Fact]
    public void ExternalLogin_WhenProviderSupported_ReturnsChallengeWithSafeReturnUrl()
    {
        var signInManager = CreateSignInManager();
        var externalLoginService = Substitute.For<IExternalLoginService>();
        var providerStore = Substitute.For<IExternalLoginProviderStore>();
        providerStore.TryGetProvider(ExternalLoginProvider.GitHubRouteSegment, out Arg.Any<ExternalLoginProvider>())
            .Returns(callInfo =>
            {
                callInfo[1] = new ExternalLoginProvider(ExternalLoginProvider.GitHubRouteSegment, ExternalLoginProvider.GitHubScheme, "GitHub");
                return true;
            });
        signInManager.ConfigureExternalAuthenticationProperties(ExternalLoginProvider.GitHubScheme, "/account/external-login/callback?returnUrl=%2Fadmin%2Fusers")
            .Returns(new AuthenticationProperties
            {
                RedirectUri = "/account/external-login/callback?returnUrl=%2Fadmin%2Fusers",
            });
        var sut = CreateSut(signInManager, externalLoginService, providerStore, static url => url == "/admin/users");

        var result = sut.ExternalLogin(ExternalLoginProvider.GitHubRouteSegment, "/admin/users");

        var challenge = Assert.IsType<ChallengeResult>(result);
        Assert.Equal([ExternalLoginProvider.GitHubScheme], challenge.AuthenticationSchemes);
        Assert.Equal("/account/external-login/callback?returnUrl=%2Fadmin%2Fusers", challenge.Properties?.RedirectUri);

        providerStore.Received(1).TryGetProvider(ExternalLoginProvider.GitHubRouteSegment, out Arg.Any<ExternalLoginProvider>());
    }

    [Fact]
    public void ExternalLogin_WhenProviderUnsupported_RedirectsToLoginWithExternalLoginError()
    {
        var signInManager = CreateSignInManager();
        var externalLoginService = Substitute.For<IExternalLoginService>();
        var providerStore = Substitute.For<IExternalLoginProviderStore>();
        providerStore.TryGetProvider("google", out Arg.Any<ExternalLoginProvider>()).Returns(false);
        var sut = CreateSut(signInManager, externalLoginService, providerStore, static url => url == "/admin/users");

        var result = sut.ExternalLogin("google", "/admin/users");

        var redirect = Assert.IsType<RedirectResult>(result);
        var uri = new Uri($"https://localhost{redirect.Url}");
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("/login", uri.AbsolutePath);
        Assert.Equal("external_login_failed", query["error"].ToString());
        Assert.Equal("/admin/users", query["returnUrl"].ToString());

        providerStore.Received(1).TryGetProvider("google", out Arg.Any<ExternalLoginProvider>());
    }

    [Fact]
    public void ExternalLogin_WhenReturnUrlUnsafe_DropsReturnUrlFromCallbackRedirect()
    {
        var signInManager = CreateSignInManager();
        var externalLoginService = Substitute.For<IExternalLoginService>();
        var providerStore = Substitute.For<IExternalLoginProviderStore>();
        providerStore.TryGetProvider(ExternalLoginProvider.GitHubRouteSegment, out Arg.Any<ExternalLoginProvider>())
            .Returns(callInfo =>
            {
                callInfo[1] = new ExternalLoginProvider(ExternalLoginProvider.GitHubRouteSegment, ExternalLoginProvider.GitHubScheme, "GitHub");
                return true;
            });
        signInManager.ConfigureExternalAuthenticationProperties(ExternalLoginProvider.GitHubScheme, "/account/external-login/callback")
            .Returns(new AuthenticationProperties
            {
                RedirectUri = "/account/external-login/callback",
            });
        var sut = CreateSut(signInManager, externalLoginService, providerStore, static _ => false);

        var result = sut.ExternalLogin(ExternalLoginProvider.GitHubRouteSegment, "https://evil.example");

        var challenge = Assert.IsType<ChallengeResult>(result);
        Assert.Equal("/account/external-login/callback", challenge.Properties?.RedirectUri);
    }

    [Fact]
    public async Task ExternalLoginCallback_WhenExternalSignInSucceeds_LocalRedirectsToSafeReturnUrl()
    {
        var signInManager = CreateSignInManager();
        var externalLoginService = Substitute.For<IExternalLoginService>();
        externalLoginService.SignInAsync(Arg.Any<CancellationToken>())
            .Returns(new ExternalLoginResult(true, null));
        var providerStore = Substitute.For<IExternalLoginProviderStore>();
        var sut = CreateSut(signInManager, externalLoginService, providerStore, static url => url == "/admin/users");

        var result = await sut.ExternalLoginCallback("/admin/users", CancellationToken.None);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/admin/users", redirect.Url);
        await externalLoginService.Received(1).SignInAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExternalLoginCallback_WhenExternalSignInFails_RedirectsToLoginWithExternalLoginError()
    {
        var signInManager = CreateSignInManager();
        var externalLoginService = Substitute.For<IExternalLoginService>();
        externalLoginService.SignInAsync(Arg.Any<CancellationToken>())
            .Returns(new ExternalLoginResult(false, "external_login_failed"));
        var providerStore = Substitute.For<IExternalLoginProviderStore>();
        var sut = CreateSut(signInManager, externalLoginService, providerStore, static url => url == "/admin/users");

        var result = await sut.ExternalLoginCallback("/admin/users", CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        var uri = new Uri($"https://localhost{redirect.Url}");
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("/login", uri.AbsolutePath);
        Assert.Equal("external_login_failed", query["error"].ToString());
        Assert.Equal("/admin/users", query["returnUrl"].ToString());
    }

    [Fact]
    public async Task ExternalLoginCallback_WhenReturnUrlUnsafe_RedirectsToRoot()
    {
        var signInManager = CreateSignInManager();
        var externalLoginService = Substitute.For<IExternalLoginService>();
        externalLoginService.SignInAsync(Arg.Any<CancellationToken>())
            .Returns(new ExternalLoginResult(true, null));
        var providerStore = Substitute.For<IExternalLoginProviderStore>();
        var sut = CreateSut(signInManager, externalLoginService, providerStore, static _ => false);

        var result = await sut.ExternalLoginCallback("https://evil.example", CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/", redirect.Url);
    }

    private static AccountController CreateSut(
        SignInManager<BzsUser> signInManager,
        IExternalLoginService externalLoginService,
        IExternalLoginProviderStore providerStore,
        Func<string, bool> isLocalUrl)
    {
        return CreateSut(signInManager, Substitute.For<IUserService>(), externalLoginService, providerStore, isLocalUrl);
    }

    private static AccountController CreateSut(
        SignInManager<BzsUser> signInManager,
        IUserService userService,
        IExternalLoginService externalLoginService,
        IExternalLoginProviderStore providerStore,
        Func<string, bool> isLocalUrl)
    {
        var urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.IsLocalUrl(Arg.Any<string>())
            .Returns(callInfo => isLocalUrl(callInfo.Arg<string>()));

        return new AccountController(
            signInManager,
            userService,
            externalLoginService,
            providerStore,
            Substitute.For<ILogger<AccountController>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
            Url = urlHelper,
        };
    }

    private static AccountController CreateSut(SignInManager<BzsUser> signInManager, Func<string, bool> isLocalUrl)
    {
        return CreateSut(
            signInManager,
            Substitute.For<IUserService>(),
            Substitute.For<IExternalLoginService>(),
            Substitute.For<IExternalLoginProviderStore>(),
            isLocalUrl);
    }

    private static AccountController CreateSut(SignInManager<BzsUser> signInManager, IUserService userService, Func<string, bool> isLocalUrl)
    {
        return CreateSut(
            signInManager,
            userService,
            Substitute.For<IExternalLoginService>(),
            Substitute.For<IExternalLoginProviderStore>(),
            isLocalUrl);
    }

    private static SignInManager<BzsUser> CreateSignInManager()
    {
        return Substitute.For<SignInManager<BzsUser>>(
            new TestUserManager(),
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<BzsUser>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<BzsUser>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<BzsUser>>());
    }
}
