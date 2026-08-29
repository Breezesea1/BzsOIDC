using BzsOIDC.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BzsOIDC.Shared.Infrastructure.Http;

/// <summary>Normalizes MVC validation and problem results at the API boundary.</summary>
public sealed class ApiProblemDetailsResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (ApiProblemDetailsWriter.IsApiRequest(context.HttpContext.Request) &&
            context.Result is ObjectResult objectResult)
        {
            var status = objectResult.StatusCode ?? StatusCodes.Status500InternalServerError;
            if (objectResult.Value is ValidationProblemDetails validation)
            {
                var errors = validation.Errors.ToDictionary(
                    static item => item.Key,
                    static _ => new[] { ApiErrorCodes.ValidationFailed },
                    StringComparer.Ordinal);
                context.Result = new ObjectResult(new ApiProblemDetails(
                    status,
                    ApiErrorCodes.ValidationFailed,
                    context.HttpContext.TraceIdentifier,
                    errors))
                {
                    StatusCode = status,
                    ContentTypes = { "application/problem+json" },
                };
            }
            else if (objectResult.Value is ApiProblemDetails)
            {
                // Controllers may provide a stable domain error code (for example
                // invalid_credentials) that must not be replaced by the status map.
            }
            else if (objectResult.Value is Microsoft.AspNetCore.Mvc.ProblemDetails || status >= StatusCodes.Status400BadRequest)
            {
                context.Result = new ObjectResult(new ApiProblemDetails(
                    status,
                    ApiProblemDetailsWriter.CodeForStatus(status),
                    context.HttpContext.TraceIdentifier))
                {
                    StatusCode = status,
                    ContentTypes = { "application/problem+json" },
                };
            }
        }

        await next();
    }
}
