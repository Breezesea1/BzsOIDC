using BzsOIDC.Contracts;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
[Route("api/account")]
[EnableRateLimiting("account")]
public sealed class AccountApiController(
    SignInManager<BzsUser> signInManager,
    IUserService userService,
    IExternalLoginProviderStore externalLoginProviderStore,
    IExternalLoginService externalLoginService) : ControllerBase
{
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Problem(ApiErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest);
        }

        var result = await signInManager.PasswordSignInAsync(
            request.UserName.Trim(), request.Password, request.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Problem(ApiErrorCodes.InvalidCredentials, StatusCodes.Status401Unauthorized);
        }

        return Ok(new AccountActionResponse(SafeReturnUrl(request.ReturnUrl)));
    }

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.ConfirmPassword))
        {
            return Problem(ApiErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest);
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Problem(ApiErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest,
                new Dictionary<string, string[]> { ["confirmPassword"] = [ApiErrorCodes.ValidationFailed] });
        }

        var createResult = await userService.CreateAsync(
            request.UserName.Trim(), request.Password, request.Email.Trim(), cancellationToken);
        if (!createResult.Succeeded)
        {
            return Problem(ApiErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest);
        }

        var signInResult = await signInManager.PasswordSignInAsync(
            request.UserName.Trim(), request.Password, isPersistent: false, lockoutOnFailure: false);
        if (!signInResult.Succeeded)
        {
            return Problem(ApiErrorCodes.InvalidCredentials, StatusCodes.Status401Unauthorized);
        }

        return Ok(new AccountActionResponse(SafeReturnUrl(request.ReturnUrl)));
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutRequest? request)
    {
        await signInManager.SignOutAsync();
        return Ok(new AccountActionResponse(SafeReturnUrl(request?.ReturnUrl)));
    }

    [HttpGet("external/providers")]
    [HttpGet("external-providers")]
    public ActionResult<IReadOnlyList<ExternalLoginProviderResponse>> GetExternalProviders()
    {
        return Ok(externalLoginProviderStore.GetEnabledProviders()
            .Select(static provider => new ExternalLoginProviderResponse(
                provider.RouteSegment, provider.Scheme, provider.DisplayName))
            .ToArray());
    }

    [HttpGet("external/{provider}")]
    public IActionResult External([FromRoute] string provider, [FromQuery] string? returnUrl)
    {
        if (!externalLoginProviderStore.TryGetProvider(provider, out var loginProvider))
        {
            return Problem(ApiErrorCodes.NotFound, StatusCodes.Status404NotFound);
        }

        var callback = "/api/account/external-login/callback";
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            callback = QueryHelpers.AddQueryString(callback, "returnUrl", returnUrl);
        }

        var properties = signInManager.ConfigureExternalAuthenticationProperties(loginProvider.Scheme, callback);
        return Challenge(properties, loginProvider.Scheme);
    }

    [HttpGet("external-login/callback")]
    public async Task<IActionResult> ExternalCallback([FromQuery] string? returnUrl, CancellationToken cancellationToken)
    {
        var result = await externalLoginService.SignInAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.ErrorCode ?? ApiErrorCodes.InvalidCredentials, StatusCodes.Status401Unauthorized);
        }

        return Ok(new AccountActionResponse(SafeReturnUrl(returnUrl)));
    }

    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

    private ObjectResult Problem(string code, int statusCode, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        return new ObjectResult(new ApiProblemDetails(statusCode, code, HttpContext.TraceIdentifier, errors))
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
