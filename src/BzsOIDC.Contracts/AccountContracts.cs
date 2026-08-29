using System.Text.Json.Serialization;

namespace BzsOIDC.Contracts;

public sealed record LoginRequest(
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("rememberMe")] bool RememberMe = false,
    [property: JsonPropertyName("returnUrl")] string? ReturnUrl = null);

public sealed record RegisterRequest(
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("confirmPassword")] string ConfirmPassword,
    [property: JsonPropertyName("returnUrl")] string? ReturnUrl = null);

public sealed record LogoutRequest(
    [property: JsonPropertyName("returnUrl")] string? ReturnUrl = null);

public sealed record AccountActionResponse(
    [property: JsonPropertyName("redirectUri")] string RedirectUri = "/");

public sealed record ExternalLoginProviderResponse(
    [property: JsonPropertyName("routeSegment")] string RouteSegment,
    [property: JsonPropertyName("scheme")] string Scheme,
    [property: JsonPropertyName("displayName")] string DisplayName);
