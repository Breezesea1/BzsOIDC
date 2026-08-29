using System.Text.Json;
using BzsOIDC.Contracts;
using BzsOIDC.Shared.Infrastructure.Http;
using Microsoft.AspNetCore.Http;

namespace BzsOIDC.Idp.UnitTests.Contracts;

public sealed class ApiContractsTests
{
    [Fact]
    public void PageRequest_DefaultsToBoundedServerPaging()
    {
        var request = new PageRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(PageRequest.DefaultPageSize, request.PageSize);
        Assert.Empty(request.Sort);
        Assert.Empty(request.Filters);
        Assert.Equal(PageRequest.MaximumPageSize, 100);
    }

    [Fact]
    public void PageResult_ComputesTotalPagesWithoutExposingPersistenceTypes()
    {
        var result = new PageResult<string>(["one"], page: 2, pageSize: 25, totalCount: 51);

        Assert.Equal(3, result.TotalPages);
        Assert.Equal("one", Assert.Single(result.Items));
    }

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

    [Fact]
    public void PermissionTopologyContracts_AreBrowserSafeAndPreserveRelationships()
    {
        var detail = new ProtectedResourceDetail
        {
            Key = "users",
            Permissions =
            [
                new PermissionDefinitionSummary
                {
                    Name = "users.read",
                    ReleaseScopes = ["api"],
                    AssignedRoles = [new RolePermissionAssignment { RoleName = "Administrator", Assigned = true }]
                }
            ]
        };

        var permission = Assert.Single(detail.Permissions);
        Assert.Equal("users", detail.Key);
        Assert.Equal("users.read", permission.Name);
        Assert.True(Assert.Single(permission.AssignedRoles).Assigned);
    }
}
