namespace BzsOIDC.AppHost;

public static class AppHostModelSettings
{
    public static bool IsSmokeProfileEnabled(string? smokeEnabled)
    {
        return string.Equals(smokeEnabled, bool.TrueString, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveCacheType(string? smokeEnabled)
    {
        return IsSmokeProfileEnabled(smokeEnabled)
            ? "Memory"
            : "Redis";
    }

    public static string ResolveIdentityConnectionString(string? smokeEnabled, string postgresConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postgresConnectionString);

        return IsSmokeProfileEnabled(smokeEnabled)
            ? "Data Source=smoke-idp.db"
            : postgresConnectionString;
    }

    public static bool ShouldUsePersistentPostgresVolume()
    {
        return true;
    }
}
