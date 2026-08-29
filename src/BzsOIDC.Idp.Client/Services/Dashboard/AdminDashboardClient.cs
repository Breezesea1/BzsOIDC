using System.Net;
using System.Net.Http.Json;

namespace BzsOIDC.Idp.Client.Services.Dashboard;

public interface IAdminDashboardClient
{
    Task<AdminDashboardSummaryResult> GetSummaryAsync(CancellationToken cancellationToken = default);
}

public sealed class AdminDashboardSummaryModel
{
    public int TotalUsers { get; init; }
    public int AdminUsers { get; init; }
    public int StandardUsers { get; init; }
    public int TotalClients { get; init; }
    public int InteractiveClients { get; init; }
    public int MachineClients { get; init; }
    public int TotalPermissionMappings { get; init; }
    public int TotalConfiguredScopes { get; init; }
}

public enum AdminDashboardSummaryStatus
{
    Success,
    RequiresLogin,
    AccessDenied,
    Unavailable
}

public sealed class AdminDashboardSummaryResult
{
    private AdminDashboardSummaryResult(AdminDashboardSummaryStatus status, AdminDashboardSummaryModel? summary)
    {
        Status = status;
        Summary = summary;
    }

    public AdminDashboardSummaryStatus Status { get; }

    public AdminDashboardSummaryModel? Summary { get; }

    public static AdminDashboardSummaryResult Success(AdminDashboardSummaryModel summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new AdminDashboardSummaryResult(AdminDashboardSummaryStatus.Success, summary);
    }

    public static AdminDashboardSummaryResult RequiresLogin()
    {
        return new AdminDashboardSummaryResult(AdminDashboardSummaryStatus.RequiresLogin, null);
    }

    public static AdminDashboardSummaryResult AccessDenied()
    {
        return new AdminDashboardSummaryResult(AdminDashboardSummaryStatus.AccessDenied, null);
    }

    public static AdminDashboardSummaryResult Unavailable()
    {
        return new AdminDashboardSummaryResult(AdminDashboardSummaryStatus.Unavailable, null);
    }
}

internal sealed class AdminDashboardClient(HttpClient httpClient) : IAdminDashboardClient
{
    public async Task<AdminDashboardSummaryResult> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("/api/admin/dashboard/summary", cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return AdminDashboardSummaryResult.RequiresLogin();
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return AdminDashboardSummaryResult.AccessDenied();
            }

            if (!response.IsSuccessStatusCode)
            {
                return AdminDashboardSummaryResult.Unavailable();
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mediaType, "text/json", StringComparison.OrdinalIgnoreCase))
            {
                return AdminDashboardSummaryResult.Unavailable();
            }

            var summary = await response.Content.ReadFromJsonAsync<AdminDashboardSummaryModel>(cancellationToken: cancellationToken);
            return summary is null
                ? AdminDashboardSummaryResult.Unavailable()
                : AdminDashboardSummaryResult.Success(summary);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AdminDashboardSummaryResult.Unavailable();
        }
        catch (HttpRequestException)
        {
            return AdminDashboardSummaryResult.Unavailable();
        }
    }
}
