using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace BzsOIDC.Idp.Controllers;

[Route("account")]
public sealed class AccountController(
    SignInManager<BzsUser> signInManager,
    IUserService userService,
    IExternalLoginService externalLoginService,
    IExternalLoginProviderStore externalLoginProviderStore,
    ILogger<AccountController> logger) : Controller
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromForm] LoginForm form, [FromQuery] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(form.UserName) || string.IsNullOrWhiteSpace(form.Password))
        {
            return RedirectToLogin(returnUrl, "validation");
        }

        var userName = form.UserName.Trim();
        var result = await signInManager.PasswordSignInAsync(userName, form.Password, form.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return RedirectToSafeLocal(returnUrl);
        }

        return RedirectToLogin(returnUrl, "invalid_credentials");
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromForm] RegisterForm form, [FromQuery] string? returnUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.UserName) ||
            string.IsNullOrWhiteSpace(form.Email) ||
            string.IsNullOrWhiteSpace(form.Password) ||
            string.IsNullOrWhiteSpace(form.ConfirmPassword))
        {
            return RedirectToRegister(returnUrl, "validation");
        }

        var userName = form.UserName.Trim();
        var email = form.Email.Trim();
        if (!string.Equals(form.Password, form.ConfirmPassword, StringComparison.Ordinal))
        {
            return RedirectToRegister(returnUrl, "password_mismatch");
        }

        var result = await userService.CreateAsync(userName, form.Password, email, cancellationToken);
        if (!result.Succeeded)
        {
            return RedirectToRegister(returnUrl, "registration_failed");
        }

        var signInResult = await signInManager.PasswordSignInAsync(userName, form.Password, false, false);
        if (!signInResult.Succeeded)
        {
            return RedirectToLogin(returnUrl, "invalid_credentials");
        }

        return RedirectToSafeLocal(returnUrl);
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public IActionResult Logout([FromQuery] string? returnUrl)
    {
        return SignOut(
            new AuthenticationProperties { RedirectUri = IsSafeLocalUrl(returnUrl) ? returnUrl : "/" },
            IdentityConstants.ApplicationScheme);
    }

    [HttpPost("external-login/{provider}")]
    public IActionResult ExternalLogin([FromRoute] string provider, [FromQuery] string? returnUrl)
    {
        if (!externalLoginProviderStore.TryGetProvider(provider, out var externalLoginProvider))
        {
            logger.LogWarning("External login provider '{Provider}' is not enabled. ReturnUrl: {ReturnUrl}", provider, returnUrl);
            return RedirectToLogin(returnUrl, "external_login_failed");
        }

        var callbackUrl = BuildExternalLoginCallbackUrl(returnUrl);
        var properties = signInManager.ConfigureExternalAuthenticationProperties(externalLoginProvider.Scheme, callbackUrl);
        return Challenge(properties, externalLoginProvider.Scheme);
    }

    [HttpGet("external-login/callback")]
    public async Task<IActionResult> ExternalLoginCallback([FromQuery] string? returnUrl, CancellationToken cancellationToken)
    {
        var result = await externalLoginService.SignInAsync(cancellationToken);
        if (result.Succeeded)
        {
            return RedirectToSafeLocal(returnUrl);
        }

        logger.LogWarning("External login callback failed with error code '{ErrorCode}'. ReturnUrl: {ReturnUrl}", result.ErrorCode ?? "external_login_failed", returnUrl);

        return RedirectToLogin(returnUrl, result.ErrorCode ?? "external_login_failed");
    }

    private IActionResult RedirectToLogin(string? returnUrl, string error)
    {
        return RedirectToAuthPage("/login", returnUrl, error);
    }

    private IActionResult RedirectToRegister(string? returnUrl, string error)
    {
        return RedirectToAuthPage("/register", returnUrl, error);
    }

    private IActionResult RedirectToAuthPage(string path, string? returnUrl, string error)
    {
        var queryValues = new Dictionary<string, string?>
        {
            ["error"] = error,
        };

        if (IsSafeLocalUrl(returnUrl))
        {
            queryValues["returnUrl"] = returnUrl;
        }

        IDictionary<string, string?> safeQueryValues = queryValues;
        var redirectPath = QueryHelpers.AddQueryString(
            path,
            safeQueryValues
                .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
                .ToDictionary(static item => item.Key, static item => item.Value));

        return Redirect(redirectPath);
    }

    private IActionResult RedirectToSafeLocal(string? returnUrl)
    {
        if (IsSafeLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl!);
        }

        return Redirect("/");
    }

    private bool IsSafeLocalUrl(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl);
    }

    private string BuildExternalLoginCallbackUrl(string? returnUrl)
    {
        if (!IsSafeLocalUrl(returnUrl))
        {
            return "/account/external-login/callback";
        }

        return QueryHelpers.AddQueryString("/account/external-login/callback", new Dictionary<string, string?>
        {
            ["returnUrl"] = returnUrl,
        });
    }

    public sealed class LoginForm
    {
        public string UserName { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public bool RememberMe { get; init; }
    }

    public sealed class RegisterForm
    {
        public string UserName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string ConfirmPassword { get; init; } = string.Empty;
    }
}
