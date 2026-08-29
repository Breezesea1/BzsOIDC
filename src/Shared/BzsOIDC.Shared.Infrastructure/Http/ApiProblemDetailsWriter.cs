using System.Text.Json;
using BzsOIDC.Contracts;
using Microsoft.AspNetCore.Http;

namespace BzsOIDC.Shared.Infrastructure.Http;

/// <summary>Writes the deliberately small, non-localized API error envelope.</summary>
public static class ApiProblemDetailsWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    public static Task WriteAsync(HttpContext context, int statusCode, string code,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.Remove("Location");

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new ApiProblemDetails(statusCode, code, context.TraceIdentifier, errors),
            SerializerOptions,
            context.RequestAborted);
    }

    public static string CodeForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized => ApiErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ApiErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ApiErrorCodes.NotFound,
        StatusCodes.Status409Conflict => ApiErrorCodes.Conflict,
        StatusCodes.Status412PreconditionFailed => ApiErrorCodes.ConcurrencyConflict,
        StatusCodes.Status428PreconditionRequired => ApiErrorCodes.PreconditionRequired,
        _ => ApiErrorCodes.Unexpected,
    };
}
