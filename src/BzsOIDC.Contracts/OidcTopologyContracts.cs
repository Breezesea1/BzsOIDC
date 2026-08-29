namespace BzsOIDC.Contracts;

public sealed record OidcTopologyResponse(
    DateTimeOffset ObservedAt,
    IReadOnlyList<OidcTopologyClientSummary> Clients,
    IReadOnlyList<OidcTopologyScopeSummary> Scopes,
    int PermissionCount,
    IReadOnlyList<string> SourceErrors);

public sealed record OidcTopologyClientSummary(
    string ClientId,
    string? DisplayName,
    string AuthFlow,
    IReadOnlyList<string> Scopes);

public sealed record OidcTopologyScopeSummary(
    string Name,
    string? DisplayName,
    string? Description,
    IReadOnlyList<string> Resources,
    IReadOnlyList<string> Clients,
    IReadOnlyList<string> Permissions);

public sealed record OidcTopologyNodeResponse(
    DateTimeOffset ObservedAt,
    string NodeType,
    string NodeId,
    object? Node,
    IReadOnlyList<string> SourceErrors);
