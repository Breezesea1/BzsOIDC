using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Authorization;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Controllers;

public sealed class RolesControllerTests
{
    [Fact]
    public async Task GetAll_WhenServiceReturnsRoles_ReturnsOkAndRequiresReadPermission()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetAllRolesAsync(Arg.Any<CancellationToken>())
            .Returns([new RoleResponse { Id = Guid.NewGuid(), Name = "admin" }]);
        var sut = new RolesController(service);

        var result = await sut.GetAll(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<RoleResponse>>(ok.Value);
        Assert.Single(payload);
        AssertActionRequiresPermission(nameof(RolesController.GetAll), PermissionConstants.RolesRead);
    }

    [Fact]
    public async Task GetById_WhenRoleMissing_ReturnsNotFound()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetRoleByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RoleResponse?)null);
        var sut = new RolesController(service);

        var result = await sut.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_WhenValid_ReturnsCreatedAtActionAndRequiresWritePermission()
    {
        var roleId = Guid.NewGuid();
        var service = Substitute.For<IPermissionTopology>();
        service.CreateRoleAsync(Arg.Any<RoleUpsertRequest>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<RoleResponse>.Success(new RoleResponse { Id = roleId, Name = "operators" }));
        var sut = new RolesController(service);

        var result = await sut.Create(new RoleUpsertRequest { Name = "operators" }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(RolesController.GetById), created.ActionName);
        AssertActionRequiresPermission(nameof(RolesController.Create), PermissionConstants.RolesWrite);
    }

    [Fact]
    public async Task Create_WhenValidationFails_ReturnsValidationProblem()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.CreateRoleAsync(Arg.Any<RoleUpsertRequest>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<RoleResponse>.Failure(
                PermissionTopologyCommandStatus.ValidationFailed,
                "Role name is required."));
        var sut = new RolesController(service);

        var result = await sut.Create(new RoleUpsertRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.IsType<ValidationProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task Create_WhenConflict_ReturnsConflict()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.CreateRoleAsync(Arg.Any<RoleUpsertRequest>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<RoleResponse>.Failure(
                PermissionTopologyCommandStatus.Conflict,
                "Role exists."));
        var sut = new RolesController(service);

        var result = await sut.Create(new RoleUpsertRequest { Name = "operators" }, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_WhenSuccess_ReturnsOk()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.UpdateRoleAsync(Arg.Any<Guid>(), Arg.Any<RoleUpsertRequest>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<RoleResponse>.Success(new RoleResponse { Id = Guid.NewGuid(), Name = "support" }));
        var sut = new RolesController(service);

        var result = await sut.Update(Guid.NewGuid(), new RoleUpsertRequest { Name = "support" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_WhenMissing_ReturnsNotFound()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.UpdateRoleAsync(Arg.Any<Guid>(), Arg.Any<RoleUpsertRequest>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<RoleResponse>.Failure(PermissionTopologyCommandStatus.NotFound, "Missing."));
        var sut = new RolesController(service);

        var result = await sut.Update(Guid.NewGuid(), new RoleUpsertRequest { Name = "support" }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Delete_WhenSuccess_ReturnsNoContent()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.DeleteRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<RoleResponse>.Success(new RoleResponse()));
        var sut = new RolesController(service);

        var result = await sut.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_WhenProtected_ReturnsConflict()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.DeleteRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<RoleResponse>.Failure(PermissionTopologyCommandStatus.Protected, "Protected."));
        var sut = new RolesController(service);

        var result = await sut.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task GetPermissions_WhenMissing_ReturnsNotFound()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>?)null);
        var sut = new RolesController(service);

        var result = await sut.GetPermissions(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPermissions_WhenSuccess_ReturnsPermissionsAndRequiresReadPermission()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.GetRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([PermissionConstants.UsersReadAll]);
        var sut = new RolesController(service);

        var result = await sut.GetPermissions(Guid.NewGuid(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value);
        Assert.Equal([PermissionConstants.UsersReadAll], payload);
        AssertActionRequiresPermission(nameof(RolesController.GetPermissions), PermissionConstants.RolesRead);
    }

    [Fact]
    public async Task SyncPermissions_WhenInvalid_ReturnsValidationProblem()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.SyncRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<IReadOnlyList<string>>.Failure(
                PermissionTopologyCommandStatus.ValidationFailed,
                "Permission invalid."));
        var sut = new RolesController(service);

        var result = await sut.SyncPermissions(
            Guid.NewGuid(),
            new RolePermissionSyncRequest { Permissions = ["missing.permission"] },
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task SyncPermissions_WhenRoleMissing_ReturnsNotFound()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.SyncRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<IReadOnlyList<string>>.Failure(PermissionTopologyCommandStatus.NotFound));
        var sut = new RolesController(service);

        var result = await sut.SyncPermissions(
            Guid.NewGuid(),
            new RolePermissionSyncRequest { Permissions = ["missing.permission"] },
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SyncPermissions_WhenSuccess_ReturnsNoContentAndRequiresWritePermission()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.SyncRolePermissionsAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PermissionTopologyCommandResult<IReadOnlyList<string>>.Success([PermissionConstants.UsersReadAll]));
        var sut = new RolesController(service);

        var result = await sut.SyncPermissions(
            Guid.NewGuid(),
            new RolePermissionSyncRequest { Permissions = [PermissionConstants.UsersReadAll] },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        AssertActionRequiresPermission(nameof(RolesController.SyncPermissions), PermissionConstants.RolesWrite);
    }

    private static void AssertActionRequiresPermission(string actionName, string permission)
    {
        var method = typeof(RolesController).GetMethods()
            .Single(method => method.Name == actionName);
        var attribute = Assert.Single(method.GetCustomAttributes(typeof(PermissionAuthorizeAttribute), inherit: false)
            .Cast<PermissionAuthorizeAttribute>());

        Assert.Equal(permission, attribute.Policy?.Replace(PermissionPolicyOptions.DefaultPolicyPrefix, string.Empty, StringComparison.Ordinal));
    }
}
