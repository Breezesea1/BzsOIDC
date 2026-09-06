using System.Diagnostics;
using BzsOIDC.Shared.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BzsOIDC.Shared.Infrastructure.Database;

public static class MigrateDbContextExtensions
{
    private static readonly string _activitySourceName = "DbMigrations";
    private static readonly ActivitySource _activitySource = new(_activitySourceName);


    extension(IServiceCollection sc)
    {
        /// <summary>
        /// Adds migration services to the service collection.
        /// </summary>
        public IServiceCollection AddMigration<TContext>(Func<TContext, IServiceProvider, Task>? seeder = null)
            where TContext : DbContext
        {
            // Enable migration tracing
            sc.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource(_activitySourceName));
            return sc.AddScoped<IMigrated>(sp =>
                seeder is null
                    ? new MigrationService<TContext>(sp)
                    : new MigrationService<TContext>(sp, seeder));
        }
    }


    /// <summary>
    ///   Migrates the database.
    /// </summary>
    /// <param name="sp">IServiceProvider</param>
    /// <param name="seeder">Seed function</param>
    /// <param name="cancellationToken">cancellationToken</param>
    /// <typeparam name="TContext">Type of db context</typeparam>
    internal static async Task MigrateDbContextAsync<TContext>(this IServiceProvider sp,
        Func<TContext, IServiceProvider, Task>? seeder, CancellationToken cancellationToken) where TContext : DbContext
    {
        using var scope = sp.CreateScope();
        var scopeServices = scope.ServiceProvider;
        var logger = scopeServices.GetRequiredService<ILogger<TContext>>();
        var context = scopeServices.GetRequiredService<TContext>();


        using var activity = _activitySource.StartActivity($"Migration operation {typeof(TContext).Name}");

        var retryCount = 10;
        var currentRetry = 0;
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        while (currentRetry < retryCount)
        {
            try
            {
                var strategy = context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    logger.LogInformation("Starting migration pipeline for context {DbContextName}",
                        typeof(TContext).Name);

                    var historyRepository = context.GetService<IHistoryRepository>();
                    await historyRepository.CreateIfNotExistsAsync(cancellationToken);

                    await context.Database.MigrateAsync(cancellationToken);

                    var remaining = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                    if (remaining.Length == 0)
                    {
                        logger.LogInformation("Migration pipeline completed for context {DbContextName}",
                            typeof(TContext).Name);
                    }
                    else
                    {
                        logger.LogWarning("Migration pipeline finished with {Count} pending migrations for context {DbContextName}: {Migrations}",
                            remaining.Length, typeof(TContext).Name, string.Join(", ", remaining));
                    }

                    if (seeder is not null)
                    {
                        await seeder(context, sp);
                    }
                });

                return;
            }
            catch (Exception ex)
            {
                currentRetry++;
                activity?.SetExceptionTags(ex);

                if (currentRetry >= retryCount)
                {
                    logger.LogError(ex,
                        "An error occurred while migrating the database used on context {DbContextName}",
                        typeof(TContext).Name);
                    throw;
                }

                logger.LogError(ex,
                    "An error occurred while migrating the database used on context {DbContextName}, current retry count: {RetryCount}",
                    typeof(TContext).Name, currentRetry);
                await Task.Delay(TimeSpan.FromSeconds(2 + currentRetry * 2), cancellationToken);
            }
        }
    }
}
