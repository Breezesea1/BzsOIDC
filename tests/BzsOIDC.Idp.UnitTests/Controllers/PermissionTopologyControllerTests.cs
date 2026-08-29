using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Services.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Controllers;

public sealed class PermissionTopologyControllerTests
{
    [Fact]
    public async Task GetResource_WhenResourceKeyEmpty_ReturnsValidationProblem()
    {
        var service = Substitute.For<IPermissionTopology>();
        var sut = new PermissionTopologyController(service);

        var result = await sut.GetResource(" ", CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.IsType<ValidationProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task GetResource_WhenResourceNotFound_ReturnsNotFound()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetResourceAsync("orders-api", Arg.Any<CancellationToken>())
            .Returns((ProtectedResourceResponse?)null);
        var sut = new PermissionTopologyController(service);

        var result = await sut.GetResource("orders-api", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SyncReleaseScopes_WhenScopesEmpty_ReturnsValidationProblem()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.SyncReleaseScopesAsync("orders.read", Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<PermissionDefinitionResponse>.Failure(
                PermissionTopologyCommandStatus.ValidationFailed,
                "At least one release scope is required."));
        var sut = new PermissionTopologyController(service);

        var result = await sut.SyncReleaseScopes(
            "orders.read",
            new PermissionReleaseScopesUpsertRequest { Scopes = [] },
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.IsType<ValidationProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task UpsertPermission_WhenValidRequest_ReturnsUpdatedPermission()
    {
        var service = Substitute.For<IPermissionTopology>();
        var updated = new PermissionDefinitionResponse
        {
            ResourceKey = "orders-api",
            Name = "orders.read",
            ReleaseScopes = ["orders-api"],
        };

        service.UpsertPermissionAsync("orders-api", "orders.read", Arg.Any<PermissionDefinitionUpsertRequest>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<PermissionDefinitionResponse>.Success(updated));

        var sut = new PermissionTopologyController(service);
        var request = new PermissionDefinitionUpsertRequest { DisplayName = "Read orders" };

        var result = await sut.UpsertPermission("orders-api", "orders.read", request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<PermissionDefinitionResponse>(ok.Value);
        Assert.Equal("orders.read", payload.Name);
    }

    [Fact]
    public async Task UpsertResource_WhenIfMatchMissing_ReturnsPreconditionRequiredProblem()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetResourceAsync("orders-api", Arg.Any<CancellationToken>())
            .Returns(new ProtectedResourceResponse { Key = "orders-api", ETag = "\"current\"" });
        var sut = WithHttpContext(new PermissionTopologyController(service));

        var result = await sut.UpsertResource("orders-api", new ProtectedResourceUpsertRequest(), CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result.Result).Value);
        Assert.Equal(StatusCodes.Status428PreconditionRequired, problem.Status);
        Assert.Equal("precondition_required", problem.Extensions["code"]);
    }

    [Fact]
    public async Task UpsertPermission_WhenIfMatchStale_ReturnsConcurrencyConflictProblem()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetPermissionAsync("orders.read", Arg.Any<CancellationToken>()).Returns(new PermissionDefinitionResponse { ETag = "\"current\"" });
        var sut = WithHttpContext(new PermissionTopologyController(service));
        sut.ControllerContext.HttpContext.Request.Headers[HeaderNames.IfMatch] = "\"stale\"";

        var result = await sut.UpsertPermission("orders-api", "orders.read", new PermissionDefinitionUpsertRequest(), CancellationToken.None);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result.Result).Value);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.Status);
        Assert.Equal("concurrency_conflict", problem.Extensions["code"]);
    }

    [Fact]
    public async Task SyncReleaseScopes_WhenIfMatchMatches_ReturnsUpdatedEtag()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetPermissionAsync("orders.read", Arg.Any<CancellationToken>()).Returns(new PermissionDefinitionResponse { ETag = "\"current\"" });
        var updated = new PermissionDefinitionResponse { Name = "orders.read", ETag = "\"updated\"" };
        service.SyncReleaseScopesAsync("orders.read", Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<PermissionDefinitionResponse>.Success(updated));
        var sut = WithHttpContext(new PermissionTopologyController(service));
        sut.ControllerContext.HttpContext.Request.Headers[HeaderNames.IfMatch] = "\"current\"";

        var result = await sut.SyncReleaseScopes("orders.read", new PermissionReleaseScopesUpsertRequest { Scopes = ["api"] }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("\"updated\"", sut.Response.Headers[HeaderNames.ETag].ToString());
    }

    [Fact]
    public void PermissionTopologyMutations_RequireAntiforgery()
    {
        Assert.NotNull(typeof(PermissionTopologyController).GetMethod(nameof(PermissionTopologyController.UpsertResource))
            ?.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(typeof(PermissionTopologyController).GetMethod(nameof(PermissionTopologyController.UpsertPermission))
            ?.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(typeof(PermissionTopologyController).GetMethod(nameof(PermissionTopologyController.SyncReleaseScopes))
            ?.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
    }

    private static PermissionTopologyController WithHttpContext(PermissionTopologyController controller)
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Fact]
    public async Task SyncRolePermissions_WhenServiceFails_ReturnsValidationProblem()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.SyncRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<IReadOnlyList<string>>.Failure(
                PermissionTopologyCommandStatus.ValidationFailed,
                "Permission is invalid."));
        var sut = new PermissionTopologyController(service);

        var result = await sut.SyncRolePermissions(Guid.NewGuid(), new RolePermissionSyncRequest
        {
            Permissions = ["missing.permission"],
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task SyncRolePermissions_WhenRoleMissing_ReturnsNotFound()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.SyncRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<IReadOnlyList<string>>.Failure(PermissionTopologyCommandStatus.NotFound));
        var sut = new PermissionTopologyController(service);

        var result = await sut.SyncRolePermissions(Guid.NewGuid(), new RolePermissionSyncRequest
        {
            Permissions = ["missing.permission"],
        }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetRolePermissions_WhenRoleExists_ReturnsPermissions()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(["users.read.all"]);
        var sut = new PermissionTopologyController(service);

        var result = await sut.GetRolePermissions(Guid.NewGuid(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value);
        Assert.Equal(["users.read.all"], payload);
    }

    [Fact]
    public async Task GetRolePermissions_WhenRoleMissing_ReturnsNotFound()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>?)null);
        var sut = new PermissionTopologyController(service);

        var result = await sut.GetRolePermissions(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SyncRolePermissions_WhenServiceSucceeds_ReturnsNoContent()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.SyncRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<IReadOnlyList<string>>.Success(["users.read.all"]));
        var sut = new PermissionTopologyController(service);

        var result = await sut.SyncRolePermissions(Guid.NewGuid(), new RolePermissionSyncRequest
        {
            Permissions = ["users.read.all"],
        }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
