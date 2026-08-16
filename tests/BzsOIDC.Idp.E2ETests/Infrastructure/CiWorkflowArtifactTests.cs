namespace BzsOIDC.Idp.E2ETests.Infrastructure;

[Trait("Category", "Smoke")]
public sealed class CiWorkflowArtifactTests
{
    [Fact]
    public void ReleaseBuildArtifactUpload_IncludesHiddenFilesForPlaywrightAssets()
    {
        var workflowPath = ResolveWorkflowPath();
        var workflow = File.ReadAllText(workflowPath);

        var uploadStepStart = workflow.IndexOf("- name: Upload Release build outputs for E2E jobs", StringComparison.Ordinal);
        Assert.True(uploadStepStart >= 0, "The CI workflow must define the Release build artifact upload step.");

        var nextJobStart = workflow.IndexOf("  e2e-smoke:", uploadStepStart, StringComparison.Ordinal);
        Assert.True(nextJobStart > uploadStepStart, "The Release build artifact upload step should appear before the E2E jobs.");

        var uploadStep = workflow[uploadStepStart..nextJobStart];

        Assert.Contains("include-hidden-files: true", uploadStep, StringComparison.Ordinal);
    }

    [Fact]
    public void FullE2EJob_WhenDispatchedOrStableRefIsPushed_Runs()
    {
        var workflowPath = ResolveWorkflowPath();
        var workflow = File.ReadAllText(workflowPath);

        var jobStart = workflow.IndexOf("  e2e-full:", StringComparison.Ordinal);
        Assert.True(jobStart >= 0, "The CI workflow must define the full E2E job.");

        var nextJobStart = workflow.IndexOf("  container-images:", jobStart, StringComparison.Ordinal);
        Assert.True(nextJobStart > jobStart, "The full E2E job should appear before the container image job.");

        var jobBlock = workflow[jobStart..nextJobStart];

        Assert.Contains("github.event_name == 'workflow_dispatch'", jobBlock, StringComparison.Ordinal);
        Assert.Contains("github.event_name == 'push'", jobBlock, StringComparison.Ordinal);
        Assert.Contains("github.ref == 'refs/heads/main'", jobBlock, StringComparison.Ordinal);
        Assert.Contains("startsWith(github.ref, 'refs/tags/')", jobBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveVersionJob_WhenTagIsPushed_RequiresStrictUnprefixedSemanticVersion()
    {
        var workflow = File.ReadAllText(ResolveWorkflowPath());

        var jobStart = workflow.IndexOf("  resolve-version:", StringComparison.Ordinal);
        Assert.True(jobStart >= 0, "The CI workflow must define the version resolution job.");

        var nextJobStart = workflow.IndexOf("  build-test:", jobStart, StringComparison.Ordinal);
        Assert.True(nextJobStart > jobStart, "The version resolution job should appear before the build job.");

        var jobBlock = workflow[jobStart..nextJobStart];

        Assert.Contains("tags:", workflow, StringComparison.Ordinal);
        Assert.Contains("- \"*.*.*\"", workflow, StringComparison.Ordinal);
        Assert.Contains("${GITHUB_EVENT_NAME}\" == \"push", jobBlock, StringComparison.Ordinal);
        Assert.Contains("${GITHUB_REF_TYPE}\" == \"tag", jobBlock, StringComparison.Ordinal);
        Assert.Contains("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", jobBlock, StringComparison.Ordinal);
        Assert.Contains("0.0.0-ci.${GITHUB_RUN_NUMBER}", jobBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerImages_WhenSemanticVersionTagIsValid_PublishesVersionHierarchyAndBuildMetadata()
    {
        var workflow = File.ReadAllText(ResolveWorkflowPath());

        var jobStart = workflow.IndexOf("  container-images:", StringComparison.Ordinal);
        Assert.True(jobStart >= 0, "The CI workflow must define the container image job.");

        var jobBlock = workflow[jobStart..];

        Assert.Contains("needs.e2e-smoke.result == 'success'", jobBlock, StringComparison.Ordinal);
        Assert.Contains("needs.app-startup.result == 'success'", jobBlock, StringComparison.Ordinal);
        Assert.Contains("needs.e2e-full.result == 'success'", jobBlock, StringComparison.Ordinal);
        Assert.Contains("needs.resolve-version.outputs.is-release", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp:sha-${GITHUB_SHA}", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp-migrator:sha-${GITHUB_SHA}", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp:${version}", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp:${major}.${minor}", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp:${major}", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp:latest", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp:edge", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp-migrator:${version}", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp-migrator:${major}.${minor}", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp-migrator:${major}", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp-migrator:latest", jobBlock, StringComparison.Ordinal);
        Assert.Contains("bzsoidc-idp-migrator:edge", jobBlock, StringComparison.Ordinal);
        Assert.Contains("VERSION=${{ needs.resolve-version.outputs.version }}", jobBlock, StringComparison.Ordinal);
        Assert.Contains("VCS_REF=${{ github.sha }}", jobBlock, StringComparison.Ordinal);
    }

    private static string ResolveWorkflowPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ".github", "workflows", "ci.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate .github/workflows/ci.yml from the test output directory.");
    }
}
