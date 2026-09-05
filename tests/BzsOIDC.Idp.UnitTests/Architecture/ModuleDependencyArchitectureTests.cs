using System.Reflection;
using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Client;

namespace BzsOIDC.Idp.UnitTests.Architecture;

public sealed class ModuleDependencyArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void PermissionTopology_DoNotReferenceOidcServices()
    {
        var topologyDirectory = Path.Combine(RepositoryRoot, "src", "BzsOIDC.Idp", "Services", "Identity", "PermissionTopology");
        var violations = FindForbiddenReferences(topologyDirectory, SearchOption.AllDirectories);

        Assert.True(
            violations.Count == 0,
            $"Permission topology must not reference OIDC services: {string.Join("; ", violations)}");
    }

    [Fact]
    public void ClientAssembly_DoesNotReferenceServerImplementation()
    {
        var clientAssembly = typeof(App).Assembly;
        var referencesServer = clientAssembly
            .GetReferencedAssemblies()
            .Any(reference => string.Equals(reference.Name, "BzsOIDC.Idp", StringComparison.Ordinal));

        Assert.False(referencesServer, "The client assembly must depend on contracts, not the server implementation.");
    }

    private static IReadOnlyList<string> FindForbiddenReferences(string directory, SearchOption searchOption)
    {
        return Directory.EnumerateFiles(directory, "*.cs", searchOption)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (line, index))
                .Where(item => item.line.Contains("Services.Oidc", StringComparison.Ordinal) ||
                               item.line.Contains("OpenIddict", StringComparison.Ordinal) ||
                               item.line.Contains("IOidc", StringComparison.Ordinal) ||
                               item.line.Contains("OidcClient", StringComparison.Ordinal))
                .Select(item => $"{Path.GetRelativePath(RepositoryRoot, path)}:{item.index + 1}"))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BzsOIDC.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the BzsOIDC repository root.");
    }
}
