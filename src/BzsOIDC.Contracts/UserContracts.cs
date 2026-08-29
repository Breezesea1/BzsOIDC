namespace BzsOIDC.Contracts;

public sealed record UserListResponse(
    IReadOnlyList<UserResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record UserResponse(
    Guid Id,
    string UserName,
    string? Email,
    bool IsAdmin,
    string ETag);

public sealed record UserCreateRequest(string UserName, string Password, string? Email, bool IsAdmin);

public sealed record UserUpdateRequest(string UserName, string? Email, string? Password, bool IsAdmin);
