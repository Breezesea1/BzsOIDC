using BzsOIDC.Contracts;
using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
[Route("api/admin/oidc-topology")]
[PermissionAuthorize(PermissionConstants.ClientsRead)]
public sealed class OidcAdministrationTopologyController(IOidcAdministrationTopology topology) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<OidcTopologyResponse>> Get(CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var snapshot = await topology.ReadAsync(cancellationToken);
        return Ok(new OidcTopologyResponse(
            observedAt,
            snapshot.Clients.Select(static client => new OidcTopologyClientSummary(
                client.ClientId,
                client.DisplayName,
                client.AuthFlow.ToString(),
                client.Scopes)).ToArray(),
            snapshot.Scopes.Select(static scope => new OidcTopologyScopeSummary(
                scope.Name,
                scope.DisplayName,
                scope.Description,
                scope.Resources,
                scope.Clients,
                scope.Permissions)).ToArray(),
            snapshot.PermissionCount,
            Array.Empty<string>()));
    }

    [HttpGet("{nodeType}/{nodeId}")]
    public async Task<ActionResult<OidcTopologyNodeResponse>> GetNode(
        string nodeType,
        string nodeId,
        CancellationToken cancellationToken)
    {
        var response = await Get(cancellationToken);
        if (response.Result is not OkObjectResult { Value: OidcTopologyResponse snapshot })
        {
            return StatusCode(response.Result is ObjectResult result ? result.StatusCode ?? 500 : 500);
        }

        object? node = nodeType.ToLowerInvariant() switch
        {
            "client" or "clients" => snapshot.Clients.FirstOrDefault(item => string.Equals(item.ClientId, nodeId, StringComparison.OrdinalIgnoreCase)),
            "scope" or "scopes" => snapshot.Scopes.FirstOrDefault(item => string.Equals(item.Name, nodeId, StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };
        return node is null
            ? NotFound()
            : Ok(new OidcTopologyNodeResponse(snapshot.ObservedAt, nodeType, nodeId, node, snapshot.SourceErrors));
    }
}
