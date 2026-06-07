using BzsOIDC.AppHost;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var environmentName = builder.Environment.EnvironmentName;
var adminUserName = builder.Configuration["Identity:Admin:UserName"];
var adminPassword = builder.Configuration["Identity:Admin:Password"];
var e2eTestingEnabled = builder.Configuration["Testing:E2E:Enabled"];
var smokeTestingEnabled = builder.Configuration["Testing:Smoke:Enabled"];
var gitHubClientId = builder.Configuration["Authentication:GitHub:ClientId"];
var gitHubClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"];
var gitHubCallbackPath = builder.Configuration["Authentication:GitHub:CallbackPath"];

if (string.IsNullOrWhiteSpace(adminUserName) && builder.Environment.IsDevelopment())
{
    adminUserName = "admin";
}

if (string.IsNullOrWhiteSpace(adminPassword) && builder.Environment.IsDevelopment())
{
    adminPassword = "admin123";
}

IResourceBuilder<PostgresServerResource>? postgres = null;
IResourceBuilder<PostgresDatabaseResource>? idpDatabase = null;
IResourceBuilder<RedisResource>? redis = null;

if (!AppHostModelSettings.IsSmokeProfileEnabled(smokeTestingEnabled))
{
    var postgresBuilder = builder.AddPostgres("postgres")
        .WithImageTag("17.6");

    if (AppHostModelSettings.ShouldUsePersistentPostgresVolume(e2eTestingEnabled))
    {
        postgresBuilder = postgresBuilder.WithDataVolume();
    }

    postgres = postgresBuilder;
    idpDatabase = postgres.AddDatabase("DefaultConnection");
    redis = builder.AddRedis("redis");
}

var idp = builder.AddProject<Projects.BzsOIDC_Idp>("idp")
    .WithEnvironment("DOTNET_ENVIRONMENT", environmentName)
    .WithEnvironment("Identity__Admin__UserName", adminUserName)
    .WithEnvironment("Identity__Admin__Password", adminPassword)
    .WithEnvironment("CacheOptions__CacheType", AppHostModelSettings.ResolveCacheType(e2eTestingEnabled, smokeTestingEnabled));

if (!string.IsNullOrWhiteSpace(smokeTestingEnabled))
{
    idp = idp.WithEnvironment("Testing__Smoke__Enabled", smokeTestingEnabled);
}

if (AppHostModelSettings.IsSmokeProfileEnabled(smokeTestingEnabled))
{
    idp = idp.WithEnvironment("ConnectionStrings__DefaultConnection", AppHostModelSettings.ResolveIdentityConnectionString(smokeTestingEnabled, "Host=postgres;Database=DefaultConnection"));
}

if (idpDatabase is not null)
{
    idp = idp.WithReference(idpDatabase);
}

if (redis is not null && string.Equals(AppHostModelSettings.ResolveCacheType(e2eTestingEnabled, smokeTestingEnabled), "Redis", StringComparison.Ordinal))
{
    idp = idp.WithReference(redis);
}

if (!string.IsNullOrWhiteSpace(e2eTestingEnabled))
{
    idp = idp.WithEnvironment("Testing__E2E__Enabled", e2eTestingEnabled);
}

if (!string.IsNullOrWhiteSpace(gitHubClientId))
{
    idp = idp.WithEnvironment("Authentication__GitHub__ClientId", gitHubClientId);
}

if (!string.IsNullOrWhiteSpace(gitHubClientSecret))
{
    idp = idp.WithEnvironment("Authentication__GitHub__ClientSecret", gitHubClientSecret);
}

if (!string.IsNullOrWhiteSpace(gitHubCallbackPath))
{
    idp = idp.WithEnvironment("Authentication__GitHub__CallbackPath", gitHubCallbackPath);
}

idp = idp.WithEnvironment("IdpIssuer", idp.GetEndpoint("https"));

if (AppHostModelSettings.IsSmokeProfileEnabled(smokeTestingEnabled))
{
    builder.Build().Run();
    return;
}

var idpMigrator = builder.AddProject<Projects.BzsOIDC_Idp_Migrator>("idp-migrator")
    .WithEnvironment("DOTNET_ENVIRONMENT", environmentName)
    .WithEnvironment("IdpIssuer", idp.GetEndpoint("https"))
    .WithEnvironment("Identity__Admin__UserName", adminUserName)
    .WithEnvironment("Identity__Admin__Password", adminPassword)
    .WithReference(idpDatabase!)
    .WaitFor(postgres!);

idp = idp
    .WaitFor(postgres!)
    .WaitForCompletion(idpMigrator);

if (redis is not null && string.Equals(AppHostModelSettings.ResolveCacheType(e2eTestingEnabled, smokeTestingEnabled), "Redis", StringComparison.Ordinal))
{
    idp = idp.WaitFor(redis);
}

builder.Build().Run();
