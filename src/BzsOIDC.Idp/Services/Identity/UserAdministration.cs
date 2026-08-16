using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BzsOIDC.Idp.Services.Identity;

public interface IUserAdministration
{
    Task<IReadOnlyList<UserAdministrationUser>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> CreateAsync(CreateUserAdministrationRequest request, CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> UpdateAsync(Guid userId, UpdateUserAdministrationRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> DeleteAsync(Guid userId, Guid currentUserId, CancellationToken cancellationToken = default);
}

public sealed record UserAdministrationUser(Guid Id, string UserName, string? Email, bool IsAdmin);

public sealed record CreateUserAdministrationRequest(string UserName, string Password, string? Email, bool IsAdmin);

public sealed record UpdateUserAdministrationRequest(string UserName, string? Email, string? Password, bool IsAdmin);

public sealed record UserAdministrationError(string Code, IReadOnlyList<string> Arguments);

public sealed record UserAdministrationResult(bool Succeeded, IReadOnlyList<UserAdministrationError> Errors)
{
    public static UserAdministrationResult Success() => new(true, []);

    public static UserAdministrationResult Failure(params UserAdministrationError[] errors) => new(false, errors);
}

public static class UserAdministrationErrorCodes
{
    public const string DuplicateUserName = "DuplicateUserName";
    public const string DuplicateEmail = "DuplicateEmail";
    public const string InvalidUserName = "InvalidUserName";
    public const string InvalidEmail = "InvalidEmail";
    public const string PasswordTooShort = "PasswordTooShort";
    public const string PasswordRequiresNonAlphanumeric = "PasswordRequiresNonAlphanumeric";
    public const string PasswordRequiresDigit = "PasswordRequiresDigit";
    public const string PasswordRequiresLower = "PasswordRequiresLower";
    public const string PasswordRequiresUpper = "PasswordRequiresUpper";
    public const string PasswordRequiresUniqueChars = "PasswordRequiresUniqueChars";
    public const string IdentityOperationFailed = "IdentityOperationFailed";
    public const string UserNotFound = "UserNotFound";
    public const string CurrentSessionDeletion = "CurrentSessionDeletion";
    public const string CurrentSessionAdminDemotion = "CurrentSessionAdminDemotion";
    public const string SavedUserCouldNotBeReloaded = "SavedUserCouldNotBeReloaded";
}

internal sealed class UserAdministration(
    UserManager<BzsUser> userManager,
    IdpDbContext dbContext,
    ILookupNormalizer lookupNormalizer) : IUserAdministration
{
    public async Task<IReadOnlyList<UserAdministrationUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var normalizedAdminRoleName = lookupNormalizer.NormalizeName(IdentitySeedConstants.AdminRoleName);
        var adminUserIds = dbContext.UserRoles
            .Where(userRole => dbContext.Roles.Any(role =>
                role.Id == userRole.RoleId && role.NormalizedName == normalizedAdminRoleName))
            .Select(static userRole => userRole.UserId);

        return await dbContext.Users
            .AsNoTracking()
            .OrderBy(static user => user.UserName)
            .Select(user => new UserAdministrationUser(
                user.Id,
                user.UserName ?? string.Empty,
                user.Email,
                adminUserIds.Contains(user.Id)))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserAdministrationResult> CreateAsync(
        CreateUserAdministrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

        var user = new BzsUser
        {
            UserName = request.UserName.Trim(),
            Email = NormalizeOptional(request.Email),
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return FromIdentityFailure(createResult, user.UserName, user.Email);
        }

        var persistedUser = await userManager.FindByNameAsync(user.UserName);
        if (persistedUser is null)
        {
            return UserAdministrationResult.Failure(SavedUserCouldNotBeReloaded());
        }

        return await SyncAdminRoleAsync(persistedUser, request.IsAdmin, currentUserId: null);
    }

    public async Task<UserAdministrationResult> UpdateAsync(
        Guid userId,
        UpdateUserAdministrationRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserName);

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return UserAdministrationResult.Failure(UserNotFound(userId));
        }

        user.UserName = request.UserName.Trim();
        user.Email = NormalizeOptional(request.Email);

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return FromIdentityFailure(updateResult, user.UserName, user.Email);
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var passwordResult = await EnsurePasswordAsync(user, request.Password.Trim());
            if (!passwordResult.Succeeded)
            {
                return FromIdentityFailure(passwordResult, user.UserName, user.Email);
            }
        }

        var persistedUser = await userManager.FindByNameAsync(user.UserName);
        if (persistedUser is null)
        {
            return UserAdministrationResult.Failure(SavedUserCouldNotBeReloaded());
        }

        return await SyncAdminRoleAsync(persistedUser, request.IsAdmin, currentUserId);
    }

    public async Task<UserAdministrationResult> DeleteAsync(
        Guid userId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (userId == currentUserId)
        {
            return UserAdministrationResult.Failure(CurrentSessionDeletion());
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return UserAdministrationResult.Failure(UserNotFound(userId));
        }

        var deleteResult = await userManager.DeleteAsync(user);
        return deleteResult.Succeeded
            ? UserAdministrationResult.Success()
            : FromIdentityFailure(deleteResult, user.UserName, user.Email);
    }

    private async Task<UserAdministrationResult> SyncAdminRoleAsync(BzsUser user, bool shouldBeAdmin, Guid? currentUserId)
    {
        var isAdmin = await userManager.IsInRoleAsync(user, IdentitySeedConstants.AdminRoleName);
        if (isAdmin == shouldBeAdmin)
        {
            return UserAdministrationResult.Success();
        }

        if (shouldBeAdmin)
        {
            var addResult = await userManager.AddToRoleAsync(user, IdentitySeedConstants.AdminRoleName);
            return addResult.Succeeded
                ? UserAdministrationResult.Success()
                : FromIdentityFailure(addResult, user.UserName, user.Email);
        }

        if (user.Id == currentUserId)
        {
            return UserAdministrationResult.Failure(CurrentSessionAdminDemotion());
        }

        var removeResult = await userManager.RemoveFromRoleAsync(user, IdentitySeedConstants.AdminRoleName);
        return removeResult.Succeeded
            ? UserAdministrationResult.Success()
            : FromIdentityFailure(removeResult, user.UserName, user.Email);
    }

    private async Task<IdentityResult> EnsurePasswordAsync(BzsUser user, string password)
    {
        if (await userManager.CheckPasswordAsync(user, password))
        {
            return IdentityResult.Success;
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        return await userManager.ResetPasswordAsync(user, resetToken, password);
    }

    private UserAdministrationResult FromIdentityFailure(
        IdentityResult result,
        string? userName,
        string? email)
    {
        return UserAdministrationResult.Failure(result.Errors
            .Select(error => ToApplicationError(error, userName, email))
            .ToArray());
    }

    private UserAdministrationError ToApplicationError(IdentityError error, string? userName, string? email)
    {
        return error.Code switch
        {
            nameof(IdentityErrorDescriber.DuplicateUserName) => Error(UserAdministrationErrorCodes.DuplicateUserName, userName),
            nameof(IdentityErrorDescriber.DuplicateEmail) => Error(UserAdministrationErrorCodes.DuplicateEmail, email),
            nameof(IdentityErrorDescriber.InvalidUserName) => Error(UserAdministrationErrorCodes.InvalidUserName, userName),
            nameof(IdentityErrorDescriber.InvalidEmail) => Error(UserAdministrationErrorCodes.InvalidEmail, email),
            nameof(IdentityErrorDescriber.PasswordTooShort) => Error(
                UserAdministrationErrorCodes.PasswordTooShort,
                userManager.Options.Password.RequiredLength.ToString()),
            nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric) => Error(UserAdministrationErrorCodes.PasswordRequiresNonAlphanumeric),
            nameof(IdentityErrorDescriber.PasswordRequiresDigit) => Error(UserAdministrationErrorCodes.PasswordRequiresDigit),
            nameof(IdentityErrorDescriber.PasswordRequiresLower) => Error(UserAdministrationErrorCodes.PasswordRequiresLower),
            nameof(IdentityErrorDescriber.PasswordRequiresUpper) => Error(UserAdministrationErrorCodes.PasswordRequiresUpper),
            nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => Error(
                UserAdministrationErrorCodes.PasswordRequiresUniqueChars,
                userManager.Options.Password.RequiredUniqueChars.ToString()),
            _ => Error(UserAdministrationErrorCodes.IdentityOperationFailed),
        };
    }

    private static UserAdministrationError UserNotFound(Guid userId)
    {
        return Error(UserAdministrationErrorCodes.UserNotFound, userId.ToString());
    }

    private static UserAdministrationError CurrentSessionDeletion()
    {
        return Error(UserAdministrationErrorCodes.CurrentSessionDeletion);
    }

    private static UserAdministrationError CurrentSessionAdminDemotion()
    {
        return Error(UserAdministrationErrorCodes.CurrentSessionAdminDemotion);
    }

    private static UserAdministrationError SavedUserCouldNotBeReloaded()
    {
        return Error(UserAdministrationErrorCodes.SavedUserCouldNotBeReloaded);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static UserAdministrationError Error(string code, params string?[] arguments)
    {
        return new UserAdministrationError(
            code,
            arguments.Where(static argument => !string.IsNullOrWhiteSpace(argument)).OfType<string>().ToArray());
    }
}
