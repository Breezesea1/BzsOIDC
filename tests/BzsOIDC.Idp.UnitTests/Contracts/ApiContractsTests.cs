using System.Text.Json;
using BzsOIDC.Contracts;
using BzsOIDC.Shared.Infrastructure.Http;
using Microsoft.AspNetCore.Http;

namespace BzsOIDC.Idp.UnitTests.Contracts;

public sealed class ApiContractsTests
{
    [Fact]
    public async Task ApiProblemDetailsWriter_EmitsSafeProblemJson()
    {
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;
        context.TraceIdentifier = "trace-123";

        await ApiProblemDetailsWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized);

        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        body.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<ApiProblemDetails>(body);
        Assert.NotNull(payload);
        Assert.Equal(ApiErrorCodes.Unauthorized, payload.Code);
        Assert.Equal("trace-123", payload.TraceId);
    }
}
